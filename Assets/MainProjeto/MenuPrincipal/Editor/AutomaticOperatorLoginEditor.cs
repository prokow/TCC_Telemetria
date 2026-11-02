using System;
using MainProjeto.MenuPrincipal.Scripts;
using UnityEditor;
using UnityEngine;

namespace MainProjeto.MenuPrincipal.Editor
{
    [CustomEditor(typeof(AutomaticOperatorLogin))]
    public sealed class AutomaticOperatorLoginEditor : UnityEditor.Editor
    {
        // Campos temporários do Inspector: não são gravados na cena, em assets ou no build.
        [NonSerialized] private string email = string.Empty;
        [NonSerialized] private string password = string.Empty;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Autorizar acesso automático", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Entre em Play Mode e informe a mesma conta que já cadastra participantes. " +
                "Autorize uma vez; depois basta escolher a próxima pessoa na lista.", MessageType.Info);

            var login = (AutomaticOperatorLogin)target;
            bool canAuthorize = Application.isPlaying && Application.platform == RuntimePlatform.WindowsEditor &&
                                login.isActiveAndEnabled && !login.IsAuthenticating;
            using (new EditorGUI.DisabledScope(!canAuthorize))
            {
                email = EditorGUILayout.TextField("E-mail do responsável", email);
                password = EditorGUILayout.PasswordField("Senha", password);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)))
                {
                    if (GUILayout.Button("Autorizar este computador"))
                    {
                        string temporaryPassword = password;
                        password = string.Empty;
                        Authorize(login, email, temporaryPassword);
                    }
                }
            }
            if (!string.IsNullOrEmpty(login.ConnectionStatus))
                EditorGUILayout.HelpBox(login.ConnectionStatus, MessageType.Info);
            if (Application.platform != RuntimePlatform.WindowsEditor)
                EditorGUILayout.HelpBox("Este configurador usa o armazenamento protegido do Windows.", MessageType.Info);
            if (login.IsAuthenticating) Repaint();
        }

        private async void Authorize(AutomaticOperatorLogin login, string operatorEmail, string temporaryPassword)
        {
            try { await login.AuthorizeThisDeviceAsync(operatorEmail, temporaryPassword); }
            catch (Exception) { Debug.LogWarning("Não foi possível autorizar este computador. Confira o estado do Play Mode e tente novamente."); }
            finally
            {
                temporaryPassword = null;
                if (this != null) Repaint();
            }
        }

        private void OnDisable()
        {
            email = string.Empty;
            password = string.Empty;
        }
    }
}
