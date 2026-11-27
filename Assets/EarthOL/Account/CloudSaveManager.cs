using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.UOS.Auth;
using Unity.UOS.CloudSave;
using Unity.UOS.CloudSave.Internal.Exception;
using Unity.UOS.CloudSave.Model.Files;
using UnityEngine;

namespace EarthOL.Account
{
    /// <summary>
    /// 负责当前登录玩家的 UOS 云端存档读写。
    ///
    /// 身份模型：先由 Unity Authentication 完成登录得到 PlayerId，再调用
    /// <see cref="AuthTokenManager.GenerateAccessToken(string,string)"/> 为当前 PlayerId 授权，
    /// 之后的读写全部由 UOS 依据该授权 token 隔离，客户端不接收、也无法指定他人的 PlayerId。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudSaveManager : MonoBehaviour
    {
        /// <summary>LINEAR 单存档使用的命名空间。</summary>
        public const string SaveNamespace = "Default";

        /// <summary>单存档名称（仅元数据，存档内容仍是原有 JSON）。</summary>
        public const string SaveName = "player_save";

        public static CloudSaveManager Instance { get; private set; }

        bool m_Initialized;
        bool m_Authorized;
        string m_AuthorizedPlayerId;
        readonly SemaphoreSlim m_InitGate = new SemaphoreSlim(1, 1);

        public bool IsInitialized => m_Initialized;
        public bool IsAuthorized => m_Authorized;
        public string AuthorizedPlayerId => m_AuthorizedPlayerId;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 初始化 UOS Cloud Save SDK（整个运行周期只初始化一次，支持失败后重试）。
        /// 与 Unity Services 初始化相互独立，不互相替代。
        /// </summary>
        public async Task InitializeAsync()
        {
            if (m_Initialized) return;

            await m_InitGate.WaitAsync();
            try
            {
                if (m_Initialized) return;

                await CloudSaveSDK.InitializeAsync();
                m_Initialized = true;
                Debug.Log("[UOS] Cloud Save SDK 初始化成功。");
            }
            finally
            {
                m_InitGate.Release();
            }
        }

        /// <summary>
        /// 为当前 Unity Authentication PlayerId 授权。切换账号 / 重新登录时重新授权；
        /// 同一玩家重复调用会被跳过，避免重复生成 token。
        /// </summary>
        public async Task AuthorizeAsync(string playerId, string userName)
        {
            if (!m_Initialized)
                throw new InvalidOperationException("UOS Cloud Save SDK 尚未初始化。");
            if (string.IsNullOrEmpty(playerId))
                throw new InvalidOperationException("PlayerId 为空，无法授权。");

            if (m_Authorized && string.Equals(m_AuthorizedPlayerId, playerId, StringComparison.Ordinal))
                return;

            await AuthTokenManager.GenerateAccessToken(playerId, userName ?? string.Empty);
            m_Authorized = true;
            m_AuthorizedPlayerId = playerId;
            Debug.Log($"[UOS] 授权成功，当前 PlayerId={playerId}");
        }

        /// <summary>
        /// 登出 / 切换账号前清理 UOS 身份上下文，避免下一个账号读到上一个账号的数据。
        /// 不删除云端账号与云端存档。
        /// </summary>
        public void ClearIdentity()
        {
            if (m_Initialized)
            {
                try
                {
                    AuthTokenManager.ClearToken();
                    Debug.Log("[UOS] 已清理 UOS 授权身份。");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[UOS] ClearToken 异常：" + exception);
                }
            }

            m_Authorized = false;
            m_AuthorizedPlayerId = null;
        }

        /// <summary>
        /// 读取当前授权玩家的 LINEAR 单存档。成功且云端存在数据时 Json 非空；
        /// 成功但云端尚无数据（新玩家）时 Json 为 null；失败时 Success 为 false。
        /// 未初始化或未授权时直接返回失败，绝不做任何网络调用。
        /// </summary>
        public async Task<SaveLoadResult> LoadAsync()
        {
            if (!m_Initialized)
                return SaveLoadResult.Failure("云端存档尚未初始化。");
            if (!m_Authorized)
                return SaveLoadResult.Failure("云端存档尚未授权，请重新登录。");

            try
            {
                var item = await CloudSaveSDK.Instance.Files.GetLinearAsync(SaveNamespace);
                if (item == null)
                {
                    Debug.Log("[UOS] 当前玩家暂无单存档。");
                    return SaveLoadResult.Succeeded(null);
                }

                var bytes = await CloudSaveSDK.Instance.Files.LoadBytesAsync(item.SaveId);
                if (bytes == null || bytes.Length == 0)
                    return SaveLoadResult.Succeeded(null);

                var json = Encoding.UTF8.GetString(bytes);
                Debug.Log($"[UOS] 读取存档成功，SaveId={item.SaveId}");
                return SaveLoadResult.Succeeded(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UOS] 读取存档失败：" + exception);
                return SaveLoadResult.Failure(ToChinese(exception));
            }
        }

        /// <summary>
        /// 将 JSON 保存为当前授权玩家的 LINEAR 单存档（无存档则创建，有存档则更新）。
        /// <paramref name="saveName"/> 会写入存档元数据的 Name 字段，UOS 控制台的存档列表
        /// 会展示该名称；为空时回退到固定名称 <see cref="SaveName"/>。
        /// 未初始化或未授权时阻止保存，返回 false。
        /// </summary>
        public async Task<bool> SaveAsync(string json, string saveName = null)
        {
            if (!m_Initialized)
            {
                Debug.LogError("[UOS] 保存失败：Cloud Save SDK 尚未初始化。");
                return false;
            }
            if (!m_Authorized)
            {
                Debug.LogError("[UOS] 保存失败：尚未完成用户授权。");
                return false;
            }
            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                var options = new UpdateOptions
                {
                    Name = string.IsNullOrEmpty(saveName) ? SaveName : saveName,
                    File = new FileOptions
                    {
                        UpdateFileWay = UpdateFileWay.ByFileBytes,
                        FileBytes = Encoding.UTF8.GetBytes(json)
                    }
                };

                var saveId = await CloudSaveSDK.Instance.Files.SaveLinearAsync(SaveNamespace, options);
                Debug.Log($"[UOS] 保存存档成功，SaveId={saveId}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UOS] 保存存档失败：" + exception);
                return false;
            }
        }

        /// <summary>把 UOS Cloud Save 异常翻译成清晰的中文提示。</summary>
        public static string ToChinese(Exception exception)
        {
            if (exception is CloudSaveException uos)
            {
                switch (uos.ErrorCode)
                {
                    case CommonErrorCodes.TransportError:
                    case CommonErrorCodes.Timeout:
                    case CommonErrorCodes.ServiceUnavailable:
                        return "网络异常，无法访问云端存档。";
                    case CommonErrorCodes.InvalidToken:
                    case CommonErrorCodes.TokenExpired:
                    case CommonErrorCodes.Forbidden:
                        return "登录状态已失效，请重新登录。";
                    case CommonErrorCodes.NotFound:
                        return "云端存档不存在。";
                    case CommonErrorCodes.TooManyRequests:
                        return "操作过于频繁，请稍后再试。";
                    default:
                        return "云端存档操作失败，请重试。";
                }
            }

            return "云端存档操作失败，请重试。";
        }
    }

    public sealed class SaveLoadResult
    {
        public bool Success { get; }

        /// <summary>成功时为存档 JSON（云端无存档时为 null），失败时为 null。</summary>
        public string Json { get; }

        public string Error { get; }

        public bool HasData => Success && !string.IsNullOrEmpty(Json);

        SaveLoadResult(bool success, string json, string error)
        {
            Success = success;
            Json = json;
            Error = error;
        }

        public static SaveLoadResult Succeeded(string json) => new SaveLoadResult(true, json, string.Empty);

        public static SaveLoadResult Failure(string error) => new SaveLoadResult(false, null, error);
    }
}
