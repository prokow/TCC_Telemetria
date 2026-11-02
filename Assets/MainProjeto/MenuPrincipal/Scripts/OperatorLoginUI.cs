using System;
using System.Threading;
using MainProjeto.Telemetria.Supabase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MainProjeto.MenuPrincipal.Scripts
{
    public sealed class OperatorLoginUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField emailInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text statusText;
        private SupabaseAuthService service;
        private CancellationTokenSource currentLogin;
        public event Action<SupabaseAuthSession> Authenticated;
        public event Action BackRequested;

        private void Awake()
        {
            emailInput.contentType = TMP_InputField.ContentType.EmailAddress;
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            passwordInput.restoreOriginalTextOnEscape = false;
            passwordInput.SetTextWithoutNotify(string.Empty);
            loginButton.onClick.AddListener(SignIn);
            backButton.onClick.AddListener(Back);
        }

        public void Initialize(SupabaseAuthService authService) => service = authService;
        public void ShowMessage(string message) => statusText.text = message;

        public async void SignIn()
        {
            if (currentLogin != null) return;
            if (service == null) { ShowMessage("A conexão não está configurada. Contate o responsável."); return; }
            string email = emailInput.text;
            string password = passwordInput.text;
            passwordInput.DeactivateInputField();
            passwordInput.SetTextWithoutNotify(string.Empty);
            var cancellation = new CancellationTokenSource();
            currentLogin = cancellation;
            SetBusy(true);
            ShowMessage("Entrando...");
            try
            {
                var pending = service.SignInAsync(email, password, cancellation.Token);
                password = null;
                var session = await pending;
                cancellation.Token.ThrowIfCancellationRequested();
                if (this == null || !ReferenceEquals(currentLogin, cancellation)) return;
                ShowMessage("Conta conectada.");
                Authenticated?.Invoke(session);
            }
            catch (OperationCanceledException) { }
            catch (SupabaseAuthException ex)
            {
                if (IsCurrent(cancellation)) ShowMessage(ex.Message);
            }
            catch (Exception)
            {
                if (IsCurrent(cancellation)) ShowMessage("Não foi possível entrar. Tente novamente.");
            }
            finally
            {
                password = null;
                if (IsCurrent(cancellation)) { currentLogin = null; SetBusy(false); }
                cancellation.Dispose();
            }
        }

        public void CancelLogin()
        {
            currentLogin?.Cancel();
            currentLogin = null;
            passwordInput.SetTextWithoutNotify(string.Empty);
            SetBusy(false);
        }

        private bool IsCurrent(CancellationTokenSource query)
            => this != null && ReferenceEquals(currentLogin, query) && !query.IsCancellationRequested;
        private void SetBusy(bool busy)
        {
            emailInput.interactable = !busy;
            passwordInput.interactable = !busy;
            loginButton.interactable = !busy;
        }
        private void Back() { CancelLogin(); BackRequested?.Invoke(); }
        private void OnDisable() => CancelLogin();
        private void OnDestroy()
        {
            if (loginButton != null) loginButton.onClick.RemoveListener(SignIn);
            if (backButton != null) backButton.onClick.RemoveListener(Back);
        }
    }
}
