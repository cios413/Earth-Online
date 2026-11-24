using System;
using UnityEngine;
using UnityEngine.UI;

namespace EarthOL.Account
{
    /// <summary>注册页：用户名、密码、确认密码、注册按钮、返回登录入口与密码保存提示。</summary>
    public sealed class RegisterUI : MonoBehaviour
    {
        public event Action<string, string, string> Submit;
        public event Action GoToLogin;

        InputField m_Username;
        InputField m_Password;
        InputField m_Confirm;
        Text m_Error;
        Button m_RegisterButton;
        Button m_BackButton;

        public void Build(Transform parent)
        {
            AccountUIBuilder.CreateBackground(parent, "Background", AccountUIBuilder.Background);

            var title = AccountUIBuilder.CreateText(parent, "Title", "Earth Online", 52, AccountUIBuilder.White, TextAnchor.MiddleCenter);
            AccountUIBuilder.PlaceTopCenter((RectTransform)title.transform, -120f, 900f, 72f);

            var subtitle = AccountUIBuilder.CreateText(parent, "Subtitle", "注册新账号", 34, AccountUIBuilder.Hint, TextAnchor.MiddleCenter);
            AccountUIBuilder.PlaceTopCenter((RectTransform)subtitle.transform, -205f, 900f, 54f);

            m_Username = AccountUIBuilder.CreateInputField(parent, "Username", "用户名（3-20 位，字母 / 数字 / - _ .）", InputField.ContentType.Standard);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_Username.transform, -295f, 680f, 88f);

            m_Password = AccountUIBuilder.CreateInputField(parent, "Password", "密码（8-64 位）", InputField.ContentType.Password);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_Password.transform, -420f, 680f, 88f);

            m_Confirm = AccountUIBuilder.CreateInputField(parent, "Confirm", "再次输入密码", InputField.ContentType.Password);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_Confirm.transform, -545f, 680f, 88f);

            var hint = AccountUIBuilder.CreateText(parent, "Hint",
                "密码需包含大写字母、小写字母、数字和符号。\n本游戏不提供找回密码功能，请妥善保存密码。",
                22, AccountUIBuilder.Hint, TextAnchor.UpperLeft);
            AccountUIBuilder.PlaceTopCenter((RectTransform)hint.transform, -660f, 680f, 120f);

            m_Error = AccountUIBuilder.CreateText(parent, "Error", string.Empty, 23, AccountUIBuilder.Error, TextAnchor.UpperLeft);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_Error.transform, -800f, 680f, 120f);

            m_RegisterButton = AccountUIBuilder.CreateButton(parent, "RegisterButton", "注  册", 34);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_RegisterButton.transform, -950f, 680f, 104f);

            m_BackButton = AccountUIBuilder.CreateLinkButton(parent, "BackLink", "返回登录", 24);
            AccountUIBuilder.PlaceTopCenter((RectTransform)m_BackButton.transform, -1080f, 680f, 56f);

            m_RegisterButton.onClick.AddListener(OnRegisterClicked);
            m_BackButton.onClick.AddListener(() => GoToLogin?.Invoke());
        }

        void OnRegisterClicked()
        {
            ShowError(string.Empty);
            Submit?.Invoke(m_Username.text, m_Password.text, m_Confirm.text);
        }

        public void ShowError(string message) => m_Error.text = message ?? string.Empty;

        public void SetInteractable(bool interactable)
        {
            m_RegisterButton.interactable = interactable;
            m_BackButton.interactable = interactable;
            m_Username.interactable = interactable;
            m_Password.interactable = interactable;
            m_Confirm.interactable = interactable;
        }

        public void ClearFields()
        {
            m_Username.text = string.Empty;
            m_Password.text = string.Empty;
            m_Confirm.text = string.Empty;
            m_Error.text = string.Empty;
        }
    }
}
