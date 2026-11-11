using System;
using System.Threading;
using System.Threading.Tasks;

namespace MainProjeto.Telemetria.Supabase
{
    public sealed class OperatorAuthorizationMissingException : Exception
    {
        public OperatorAuthorizationMissingException() : base("Autorize este dispositivo com a conta do responsável para exibir os participantes.") { }
    }

    public sealed class RememberedOperatorLogin
    {
        private readonly SupabaseAuthService auth;
        private readonly ISupabaseSessionStore store;
        public RememberedOperatorLogin(SupabaseAuthService auth, ISupabaseSessionStore store)
        {
            this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task<SupabaseAuthSession> RestoreAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string token = store.LoadRefreshToken();
            if (string.IsNullOrWhiteSpace(token)) throw new OperatorAuthorizationMissingException();
            var session = await auth.RefreshAsync(token, cancellationToken);
            // O Supabase rotaciona tokens: salvar a nova autorização antes de liberar a UI.
            store.SaveRefreshToken(session.RefreshToken);
            cancellationToken.ThrowIfCancellationRequested();
            return session;
        }

        public async Task<SupabaseAuthSession> AuthorizeAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = await auth.SignInAsync(email, password, cancellationToken);
            password = null;
            if (string.IsNullOrWhiteSpace(session.RefreshToken))
                throw new SupabaseAuthException(AuthFailure.InvalidResponse, "O login não retornou uma autorização renovável. Tente novamente.");
            store.SaveRefreshToken(session.RefreshToken);
            cancellationToken.ThrowIfCancellationRequested();
            return session;
        }
    }
}
