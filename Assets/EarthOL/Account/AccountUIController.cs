using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EarthOL.Account
{
    /// <summary>
    /// 账户系统门面：运行时构建登录 / 注册界面，编排“初始化 → 自动恢复或登录 → 读取云端存档 → 进入游戏”
    /// 的完整流程，并在入口页提供退出登录。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AccountUIController : MonoBehaviour
    {
        AuthenticationManager m_Auth;
        CloudSaveManager m_Save;
        PlayerDataService m_Data;
        Func<bool> m_IsSessionActive;
        Action m_ReturnToSelection;

        Canvas m_Canvas;
        LoginUI m_Login;
        RegisterUI m_Register;
        GameObject m_Loading;
        Text m_LoadingText;
        GameObject m_ErrorView;
        Text m_ErrorText;

        readonly TaskCompletionSource<bool> m_Ready = new TaskCompletionSource<bool>();
        bool m_ReadySet;
        bool m_Busy;

        public static AccountUIController Instance { get; private set; }

        /// <summary>
        /// 账户 uGUI 界面（登录 / 注册 / 加载 / 错误页）显示时返回 true。
        /// 游戏的 IMGUI 界面据此暂停绘制，避免 IMGUI 永远盖在 uGUI 之上导致账户页点不到。
        /// </summary>
        public static bool BlocksGameUi =>
            Instance != null && Instance.m_Canvas != null && Instance.m_Canvas.gameObject.activeSelf;

        /// <summary>
        /// 创建账户系统各管理器与 UI，返回控制器。由启动引导在场景首帧调用。
        /// </summary>
        public static AccountUIController Create(Func<bool> isSessionActive, Action returnToSelection)
        {
            var auth = new GameObject("Authentication").AddComponent<AuthenticationManager>();
            var save = new GameObject("CloudSave").AddComponent<CloudSaveManager>();
            var data = new GameObject("PlayerData").AddComponent<PlayerDataService>();

            var uiGo = new GameObject("AccountUI");
            var ui = uiGo.AddComponent<AccountUIController>();
            ui.Configure(auth, save, data, isSessionActive, returnToSelection);
            ui.BuildUi();
            ui.Boot();
            return ui;
        }

        public Task WaitUntilReadyAsync() => m_Ready.Task;

        public bool IsReady => m_ReadySet;

        void Awake()
        {
            Instance = this;
        }

        void Configure(
            AuthenticationManager auth,
            CloudSaveManager save,
            PlayerDataService data,
            Func<bool> isSessionActive,
            Action returnToSelection)
        {
            m_Auth = auth;
            m_Save = save;
            m_Data = data;
            m_IsSessionActive = isSessionActive;
            m_ReturnToSelection = returnToSelection;
        }

        void BuildUi()
        {
            EnsureEventSystem();

            m_Canvas = AccountUIBuilder.CreateCanvas("AccountCanvas", 200);
            m_Canvas.transform.SetParent(transform, false);

            var loginGo = AccountUIBuilder.CreateStretchedRoot(m_Canvas.transform, "LoginView").gameObject;
            m_Login = loginGo.AddComponent<LoginUI>();
            m_Login.Build(loginGo.transform);
            m_Login.Submit += OnLoginSubmit;
            m_Login.GoToRegister += ShowRegister;

            var registerGo = AccountUIBuilder.CreateStretchedRoot(m_Canvas.transform, "RegisterView").gameObject;
            m_Register = registerGo.AddComponent<RegisterUI>();
            m_Register.Build(registerGo.transform);
            m_Register.Submit += OnRegisterSubmit;
            m_Register.GoToLogin += ShowLogin;

            m_Loading = BuildMessageView("LoadingView", out m_LoadingText);
            m_ErrorView = BuildErrorView();

            m_Canvas.gameObject.SetActive(false);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            // 工程启用的是新 Input System，必须用 InputSystemUIInputModule 接收触摸；
            // StandaloneInputModule 读取的是已禁用的旧 UnityEngine.Input，手机上将完全无响应。
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        GameObject BuildMessageView(string name, out Text messageText)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(m_Canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.66f);
            image.raycastTarget = true;

            messageText = AccountUIBuilder.CreateText(go.transform, "Message", string.Empty, 32, Color.white, TextAnchor.MiddleCenter);
            var textRect = (RectTransform)messageText.transform;
            textRect.anchorMin = new Vector2(0.1f, 0.45f);
            textRect.anchorMax = new Vector2(0.9f, 0.55f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return go;
        }

        GameObject BuildErrorView()
        {
            var go = new GameObject("ErrorView", typeof(RectTransform));
            go.transform.SetParent(m_Canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = AccountUIBuilder.Background;
            image.raycastTarget = true;

            m_ErrorText = AccountUIBuilder.CreateText(go.transform, "ErrorText", string.Empty, 30, AccountUIBuilder.Error, TextAnchor.UpperLeft);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_ErrorText.transform, -300f, 820f, 260f);

            var retry = AccountUIBuilder.CreateButton(go.transform, "RetryButton", "重试读取存档", 32);
            AccountUIBuilder.PlaceTopCenter((RectTransform)retry.transform, -620f, 640f, 104f);
            retry.onClick.AddListener(() =>
            {
                if (m_Busy) return;
                _ = EnterGameAfterAuthAsync();
            });

            var logout = AccountUIBuilder.CreateLinkButton(go.transform, "ErrorLogoutButton", "退出登录", 26);
            AccountUIBuilder.PlaceTopCenter((RectTransform)logout.transform, -760f, 640f, 60f);
            logout.onClick.AddListener(Logout);

            return go;
        }

        async void Boot()
        {
            try
            {
                ShowLoading("正在连接云端服务…");
                await m_Auth.InitializeAsync();

                // 自动登录检查：存在缓存会话则恢复，否则进入登录 / 注册界面。
                ShowLoading("正在检查登录状态…");
                var auto = await m_Auth.TryAutoLoginAsync();

                if (auto.Status == AutoLoginStatus.SignedIn)
                {
                    await EnterGameAfterAuthAsync();
                }
                else if (auto.Status == AutoLoginStatus.NoSession)
                {
                    // 无缓存账号：默认显示注册页（可切到登录页）。
                    ShowRegister();
                }
                else
                {
                    // 恢复失败：回到登录页，让玩家用用户名密码重新登录。
                    ShowLogin();
                    m_Login.ShowError(auto.Error);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowLogin();
                m_Login.ShowError(AuthenticationManager.ToInitializationError(exception));
            }
        }

        async void OnLoginSubmit(string username, string password)
        {
            if (m_Busy) return;

            var check = AuthenticationManager.ValidateCredentials(username, password);
            if (!check.ok)
            {
                m_Login.ShowError(check.message);
                return;
            }

            SetBusy(true, "正在登录…");
            try { await m_Auth.InitializeAsync(); }
            catch (Exception exception)
            {
                ShowLogin();
                m_Login.ShowError(AuthenticationManager.ToInitializationError(exception));
                return;
            }
            var result = await m_Auth.SignInAsync(username, password);
            if (!result.Success)
            {
                ShowLogin();
                m_Login.ShowError(result.Error);
                return;
            }

            await EnterGameAfterAuthAsync();
        }

        async void OnRegisterSubmit(string username, string password, string confirm)
        {
            if (m_Busy) return;

            var check = AuthenticationManager.ValidateCredentials(username, password);
            if (!check.ok)
            {
                m_Register.ShowError(check.message);
                return;
            }
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                m_Register.ShowError("两次输入的密码不一致。");
                return;
            }

            SetBusy(true, "正在注册…");
            try { await m_Auth.InitializeAsync(); }
            catch (Exception exception)
            {
                ShowRegister();
                m_Register.ShowError(AuthenticationManager.ToInitializationError(exception));
                return;
            }
            var result = await m_Auth.SignUpAsync(username, password);
            if (!result.Success)
            {
                ShowRegister();
                m_Register.ShowError(result.Error);
                return;
            }

            // 注册成功：初始化玩家云端存档，并尽力写入；写入失败会在后续保存时机重试。
            if (!await PrepareCloudAsync(m_Auth.PlayerId, username)) return;

            m_Data.SetInitial(username);
            if (!await m_Data.SaveAsync())
            {
                Debug.LogWarning("[Account] 初始存档尚未写入云端，将在后续保存时机重试。");
            }

            EnterGame();
        }

        /// <summary>初始化 UOS Cloud Save 并为当前 PlayerId 授权；失败时显示错误并返回 false。</summary>
        async Task<bool> PrepareCloudAsync(string playerId, string displayName)
        {
            try
            {
                await m_Save.InitializeAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowLoadError("云端存档初始化失败：" + CloudSaveManager.ToChinese(exception));
                return false;
            }

            try
            {
                await m_Save.AuthorizeAsync(playerId, displayName);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowLoadError("云端存档授权失败：" + CloudSaveManager.ToChinese(exception));
                return false;
            }
        }

        async Task EnterGameAfterAuthAsync()
        {
            if (!await PrepareCloudAsync(m_Auth.PlayerId, m_Auth.LastUsername)) return;

            ShowLoading("正在读取云端存档…");

            var result = await m_Save.LoadAsync();
            if (!result.Success)
            {
                ShowLoadError(result.Error);
                return;
            }

            if (result.HasData)
            {
                if (!m_Data.TryApplyJson(result.Json))
                {
                    ShowLoadError("云端存档格式异常，无法解析。");
                    return;
                }

                // 让 UOS 控制台存档列表显示玩家自己起的名字（存档内容不变，仅更新存档元数据 Name）。
                if (!await m_Data.SaveAsync())
                {
                    Debug.LogWarning("[Account] 同步存档显示名失败，将在后续保存时机重试。");
                }
            }
            else
            {
                // 云端尚无存档（例如历史账号 / 首次保存未完成），建立初始存档。
                var displayName = string.IsNullOrEmpty(m_Auth.LastUsername) ? "玩家" : m_Auth.LastUsername;
                m_Data.SetInitial(displayName);
                if (!await m_Data.SaveAsync())
                {
                    Debug.LogWarning("[Account] 初始存档尚未写入云端，将在后续保存时机重试。");
                }
            }

            EnterGame();
        }

        void EnterGame()
        {
            SetBusy(false);
            m_Login.ClearFields();
            m_Register.ClearFields();
            m_Canvas.gameObject.SetActive(false);

            if (!m_ReadySet)
            {
                m_ReadySet = true;
                m_Ready.TrySetResult(true);
            }
        }

        void Logout()
        {
            m_ReturnToSelection?.Invoke();
            m_Auth.SignOut();
            m_Data.Clear();
            if (m_Save != null) m_Save.ClearIdentity();
            ShowLogin();
        }

        void ShowLogin()
        {
            m_Busy = false;
            m_Canvas.gameObject.SetActive(true);
            m_Login.gameObject.SetActive(true);
            m_Register.gameObject.SetActive(false);
            m_Loading.SetActive(false);
            m_ErrorView.SetActive(false);
            m_Login.SetInteractable(true);
        }

        void ShowRegister()
        {
            m_Busy = false;
            m_Canvas.gameObject.SetActive(true);
            m_Login.gameObject.SetActive(false);
            m_Register.gameObject.SetActive(true);
            m_Loading.SetActive(false);
            m_ErrorView.SetActive(false);
            m_Register.SetInteractable(true);
        }

        void ShowLoading(string message)
        {
            m_Canvas.gameObject.SetActive(true);
            m_Login.gameObject.SetActive(false);
            m_Register.gameObject.SetActive(false);
            m_ErrorView.SetActive(false);
            m_Loading.SetActive(true);
            m_LoadingText.text = message;
        }

        void ShowLoadError(string message)
        {
            m_Busy = false;
            m_Canvas.gameObject.SetActive(true);
            m_Login.gameObject.SetActive(false);
            m_Register.gameObject.SetActive(false);
            m_Loading.SetActive(false);
            m_ErrorView.SetActive(true);
            m_ErrorText.text = "读取云端存档失败：" + message +
                "\n\n不会重置你的游戏进度。请检查网络后重试，或退出登录。";
        }

        void SetBusy(bool busy, string loadingMessage = null)
        {
            m_Busy = busy;
            if (busy)
            {
                ShowLoading(loadingMessage);
            }
            else
            {
                m_Loading.SetActive(false);
                if (m_Login.gameObject.activeSelf) m_Login.SetInteractable(true);
                if (m_Register.gameObject.activeSelf) m_Register.SetInteractable(true);
            }
        }

        // 退出游戏 / 切后台时尽力保存（async 保存为尽力而为）。
        void OnApplicationPause(bool paused)
        {
            if (paused) SaveDirtyBestEffort();
        }

        void OnApplicationQuit() => SaveDirtyBestEffort();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (m_Login != null) m_Login.Submit -= OnLoginSubmit;
            if (m_Register != null) m_Register.Submit -= OnRegisterSubmit;
            SaveDirtyBestEffort();
        }

        void SaveDirtyBestEffort()
        {
            if (m_Auth == null || !m_Auth.IsSignedIn) return;
            if (m_Data == null || !m_Data.HasData || !m_Data.IsDirty) return;
            _ = m_Data.SaveAsync();
        }

        void OnGUI()
        {
            if (m_Auth == null || !m_Auth.IsSignedIn) return;
            if (m_Canvas != null && m_Canvas.gameObject.activeSelf) return;
            if (m_IsSessionActive != null && m_IsSessionActive()) return;

            float scale = Mathf.Min(Screen.width / 1080f, Screen.height / 1920f);
            float height = Mathf.Max(44f, 76f * scale);
            float pad = Mathf.Max(10f, 24f * scale);
            float y = Screen.height - height - Mathf.Max(pad, Screen.safeArea.yMin + pad);

            var label = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Max(14, Mathf.RoundToInt(24f * scale)),
                normal = { textColor = new Color(0.75f, 0.82f, 0.89f) }
            };
            var button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(14, Mathf.RoundToInt(24f * scale)) };

            string account = m_Data != null && m_Data.HasData ? m_Data.DisplayName : m_Auth.LastUsername;
            if (string.IsNullOrEmpty(account)) account = "玩家";

            var labelRect = new Rect(pad, y, Screen.width * 0.55f, height);
            GUI.Label(labelRect, "账号：" + account, label);

            float buttonWidth = Mathf.Max(110f, 170f * scale);
            var logoutRect = new Rect(Screen.width - pad - buttonWidth, y, buttonWidth, height);
            if (GUI.Button(logoutRect, "退出登录", button)) Logout();
        }
    }
}
