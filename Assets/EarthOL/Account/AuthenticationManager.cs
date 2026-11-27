using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace EarthOL.Account
{
    /// <summary>
    /// 负责 Unity Services 初始化、用户名密码注册 / 登录 / 登出以及登录会话自动恢复。
    /// 只使用当前 Authentication 会话，绝不创建匿名账号，也绝不在登录失败时创建新账号。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AuthenticationManager : MonoBehaviour
    {
        public static AuthenticationManager Instance { get; private set; }

        public bool IsInitialized { get; private set; }

        public bool IsSignedIn => IsInitialized && AuthenticationService.Instance.IsSignedIn;

        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : string.Empty;

        /// <summary>最近一次显式注册 / 登录使用的用户名（自动恢复时为空，显示名以存档为准）。</summary>
        public string LastUsername { get; private set; } = string.Empty;

        public event Action SignedIn;
        public event Action SignedOut;

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

        /// <summary>初始化 Unity Services（幂等）。调用后方可访问 AuthenticationService.Instance。</summary>
        public async Task InitializeAsync()
        {
            if (IsInitialized) return;
            await UnityServices.InitializeAsync();
            IsInitialized = true;
            Debug.Log("[Account] Unity Services 已初始化。");
        }

        /// <summary>
        /// 自动登录检查（必须在 <see cref="InitializeAsync"/> 之后、显示登录界面前调用）。
        /// 存在缓存 Session Token 时用 SignInAnonymouslyAsync 恢复上一个账号（不会新建匿名玩家）；
        /// 无缓存时返回 NoSession；恢复失败返回 Failed（不创建账号、不覆盖存档）。
        /// </summary>
        public async Task<AutoLoginResult> TryAutoLoginAsync()
        {
            if (!IsInitialized)
                return AutoLoginResult.Failed("服务尚未初始化。");

            try
            {
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    LastUsername = string.Empty;
                    return AutoLoginResult.SignedIn(AuthenticationService.Instance.PlayerId);
                }

                if (AuthenticationService.Instance.SessionTokenExists)
                {
                    // 存在缓存 Session Token：SignInAnonymouslyAsync 会沿用它恢复原账号，而不是新建匿名玩家。
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    LastUsername = string.Empty;
                    return AutoLoginResult.SignedIn(AuthenticationService.Instance.PlayerId);
                }

                // 无缓存会话：不调用 SignInAnonymouslyAsync，也不自动创建匿名账号。
                return AutoLoginResult.NoSession();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Account] 自动登录失败：" + exception);
                return AutoLoginResult.Failed(ToAutoLoginError(exception));
            }
        }

        public async Task<AuthResult> SignUpAsync(string username, string password)
        {
            var validation = ValidateCredentials(username, password);
            if (!validation.ok) return AuthResult.Failure(validation.message);

            try
            {
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                LastUsername = username;
                SignedIn?.Invoke();
                return AuthResult.Succeeded(AuthenticationService.Instance.PlayerId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Account] 注册失败：" + exception);
                return AuthResult.Failure(ToChinese(exception, isSignUp: true));
            }
        }

        public async Task<AuthResult> SignInAsync(string username, string password)
        {
            var validation = ValidateCredentials(username, password);
            if (!validation.ok) return AuthResult.Failure(validation.message);

            try
            {
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
                LastUsername = username;
                SignedIn?.Invoke();
                return AuthResult.Succeeded(AuthenticationService.Instance.PlayerId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Account] 登录失败：" + exception);
                return AuthResult.Failure(ToChinese(exception, isSignUp: false));
            }
        }

        /// <summary>登出并清除本地登录状态（不删除云端账号与云端存档）。</summary>
        public void SignOut()
        {
            if (!IsInitialized)
            {
                LastUsername = string.Empty;
                SignedOut?.Invoke();
                return;
            }

            try
            {
                // clearCredentials=true：同时清除缓存的会话令牌，避免下次启动被自动恢复。
                AuthenticationService.Instance.SignOut(clearCredentials: true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Account] 登出异常：" + exception);
            }
            finally
            {
                LastUsername = string.Empty;
                SignedOut?.Invoke();
            }
        }

        // ---------------------------------------------------------------------
        // 校验与中文错误提示
        // ---------------------------------------------------------------------

        public const int UsernameMinLength = 3;
        public const int UsernameMaxLength = 20;
        public const int PasswordMinLength = 8;
        public const int PasswordMaxLength = 64;

        public static (bool ok, string message) ValidateCredentials(string username, string password)
        {
            var userCheck = ValidateUsername(username);
            if (!userCheck.ok) return userCheck;
            return ValidatePassword(password);
        }

        public static (bool ok, string message) ValidateUsername(string username)
        {
            if (string.IsNullOrEmpty(username)) return (false, "请输入用户名。");
            if (username.Length < UsernameMinLength || username.Length > UsernameMaxLength)
                return (false, $"用户名长度需为 {UsernameMinLength}-{UsernameMaxLength} 个字符。");
            foreach (var ch in username)
            {
                bool valid = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                             (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.';
                if (!valid)
                    return (false, "用户名只能包含字母、数字以及 - _ .");
            }
            return (true, string.Empty);
        }

        public static (bool ok, string message) ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return (false, "请输入密码。");
            if (password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
                return (false, $"密码长度需为 {PasswordMinLength}-{PasswordMaxLength} 个字符。");

            bool hasUpper = false, hasLower = false, hasDigit = false, hasSymbol = false;
            foreach (var ch in password)
            {
                if (ch >= 'A' && ch <= 'Z') hasUpper = true;
                else if (ch >= 'a' && ch <= 'z') hasLower = true;
                else if (ch >= '0' && ch <= '9') hasDigit = true;
                else hasSymbol = true;
            }

            if (!hasUpper || !hasLower || !hasDigit || !hasSymbol)
                return (false, "密码需包含大写字母、小写字母、数字和符号。");
            return (true, string.Empty);
        }

        public static string ToChinese(Exception exception, bool isSignUp)
        {
            var code = exception is RequestFailedException failed ? failed.ErrorCode : -1;

            if (code == CommonErrorCodes.TransportError ||
                code == CommonErrorCodes.ServiceUnavailable ||
                code == CommonErrorCodes.Timeout)
                return "网络异常，请检查网络后重试。";

            if (code == CommonErrorCodes.TooManyRequests)
                return "操作过于频繁，请稍后再试。";

            if (code == CommonErrorCodes.Forbidden || code == CommonErrorCodes.ProjectPolicyAccessDenied)
                return "当前工程无权访问该服务，请联系管理员。";

            if (isSignUp)
            {
                if (code == AuthenticationErrorCodes.AccountAlreadyLinked || code == CommonErrorCodes.Conflict)
                    return "该用户名已被注册，请更换用户名。";
                if (code == AuthenticationErrorCodes.InvalidParameters)
                    return "用户名或密码格式不符合要求。";
            }

            if (code == AuthenticationErrorCodes.InvalidParameters ||
                code == CommonErrorCodes.InvalidToken ||
                code == CommonErrorCodes.InvalidRequest ||
                code == CommonErrorCodes.NotFound)
                return "登录失败：该账号不存在或密码错误，请重新登录。";

            if (code == AuthenticationErrorCodes.BannedUser)
                return "该账号已被封禁。";

            if (code == AuthenticationErrorCodes.ClientInvalidUserState)
                return "当前登录状态异常，请重试。";

            return isSignUp ? "注册失败，请稍后重试。" : "登录失败，请稍后重试。";
        }

        /// <summary>把 Unity Services 初始化阶段的异常翻译成清晰的中文提示。</summary>
        public static string ToInitializationError(Exception exception)
        {
            if (exception is ServicesInitializationException)
                return "初始化失败：工程未正确关联 Unity 云项目，或服务配置无效。";

            if (exception is RequestFailedException failed &&
                (failed.ErrorCode == CommonErrorCodes.TransportError ||
                 failed.ErrorCode == CommonErrorCodes.ServiceUnavailable ||
                 failed.ErrorCode == CommonErrorCodes.Timeout))
                return "初始化失败：网络异常，无法连接 Unity 服务，请检查网络后重试。";

            return "初始化失败，请稍后重试。";
        }

        /// <summary>把自动登录恢复失败翻译成友好提示（不暴露原始 SDK 错误）。</summary>
        public static string ToAutoLoginError(Exception exception)
        {
            var code = exception is RequestFailedException failed ? failed.ErrorCode : -1;

            if (code == CommonErrorCodes.TransportError ||
                code == CommonErrorCodes.ServiceUnavailable ||
                code == CommonErrorCodes.Timeout)
                return "网络异常，自动登录失败，请检查网络后重新登录。";

            if (code == AuthenticationErrorCodes.InvalidSessionToken ||
                code == CommonErrorCodes.InvalidToken ||
                code == CommonErrorCodes.TokenExpired)
                return "登录会话已失效，请重新登录。";

            return "自动登录失败，请重新登录。";
        }
    }

    public sealed class AuthResult
    {
        public bool Success { get; }
        public string Error { get; }
        public string PlayerId { get; }

        AuthResult(bool success, string playerId, string error)
        {
            Success = success;
            PlayerId = playerId;
            Error = error;
        }

        public static AuthResult Succeeded(string playerId) => new AuthResult(true, playerId, string.Empty);

        public static AuthResult Failure(string error) => new AuthResult(false, string.Empty, error);
    }

    public enum AutoLoginStatus
    {
        /// <summary>已恢复登录（含原本就已登录）。</summary>
        SignedIn,

        /// <summary>本设备没有缓存的登录会话。</summary>
        NoSession,

        /// <summary>存在缓存会话但恢复失败。</summary>
        Failed
    }

    public sealed class AutoLoginResult
    {
        public AutoLoginStatus Status { get; }
        public string PlayerId { get; }
        public string Error { get; }

        AutoLoginResult(AutoLoginStatus status, string playerId, string error)
        {
            Status = status;
            PlayerId = playerId;
            Error = error;
        }

        public static AutoLoginResult SignedIn(string playerId) =>
            new AutoLoginResult(AutoLoginStatus.SignedIn, playerId, string.Empty);

        public static AutoLoginResult NoSession() =>
            new AutoLoginResult(AutoLoginStatus.NoSession, string.Empty, string.Empty);

        public static AutoLoginResult Failed(string error) =>
            new AutoLoginResult(AutoLoginStatus.Failed, string.Empty, error);
    }
}
