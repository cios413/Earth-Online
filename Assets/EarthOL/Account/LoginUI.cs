using System;
using UnityEngine;
using UnityEngine.UI;

namespace EarthOL.Account
{
    /// <summary>登录页：用户名、密码、登录按钮、注册入口。</summary>
    public sealed class LoginUI : MonoBehaviour
    {
        public event Action<string, string> Submit;
        public event Action GoToRegister;

        InputField m_Username;
        InputField m_Password;
        Text m_Error;
        Button m_LoginButton;
        Button m_RegisterButton;

        public void Build(Transform parent)
        {
            AccountUIBuilder.CreateBackground(parent, "Background", AccountUIBuilder.Background);

            var title = AccountUIBuilder.CreateText(parent, "Title", "Earth Online", 58, AccountUIBuilder.White, TextAnchor.MiddleCenter);
            AccountUIBuilder.PlaceTopCenter((RectTransform)title.transform, -160f, 900f, 80f);

            var subtitle = AccountUIBuilder.CreateText(parent, "Subtitle", "登录", 34, AccountUIBuilder.Hint, TextAnchor.MiddleCenter);
            AccountUIBuilder.PlaceTopCenter((RectTransform)subtitle.transform, -250f, 900f, 54f);

            m_Username = AccountUIBuilder.CreateInputField(parent, "Username", "用户名", InputField.ContentType.Standard);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_Username.transform, -360f, 660f, 92f);

            m_Password = AccountUIBuilder.CreateInputField(parent, "Password", "密码", InputField.ContentType.Password);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_Password.transform, -490f, 660f, 92f);
            AccountUIBuilder.CreateEyeToggle(m_Password);

            m_Error = AccountUIBuilder.CreateText(parent, "Error", string.Empty, 25, AccountUIBuilder.Error, TextAnchor.UpperLeft);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_Error.transform, -620f, 660f, 150f);

            m_LoginButton = AccountUIBuilder.CreateButton(parent, "LoginButton", "登  录", 36);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_LoginButton.transform, -800f, 660f, 110f);

            m_RegisterButton = AccountUIBuilder.CreateLinkButton(parent, "RegisterLink", "还没有账号？立即注册", 26);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_RegisterButton.transform, -940f, 660f, 60f);

            m_LoginButton.onClick.AddListener(OnLoginClicked);
            m_RegisterButton.onClick.AddListener(() => GoToRegister?.Invoke());
        }

        void OnLoginClicked()
        {
            ShowError(string.Empty);
            Submit?.Invoke(m_Username.text, m_Password.text);
        }

        public void ShowError(string message) => m_Error.text = message ?? string.Empty;

        public void SetInteractable(bool interactable)
        {
            m_LoginButton.interactable = interactable;
            m_RegisterButton.interactable = interactable;
            m_Username.interactable = interactable;
            m_Password.interactable = interactable;
        }

        public void ClearFields()
        {
            m_Username.text = string.Empty;
            m_Password.text = string.Empty;
            m_Error.text = string.Empty;
        }
    }
}
