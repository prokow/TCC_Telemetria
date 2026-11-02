using System;
using System.Threading;
using System.Threading.Tasks;

namespace MainProjeto.Telemetria.Supabase
{
    public interface ISupabaseAccessSession
    {
        Task<SupabaseAuthSession> GetAsync(CancellationToken cancellationToken);
        void Invalidate();
    }
    // Compartilha e serializa renovacoes entre menu e uploader; nao depende de uma cena.
    public sealed class SharedSupabaseSession : ISupabaseAccessSession
    {
        private readonly RememberedOperatorLogin login;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        public SupabaseAuthSession Current { get; private set; }
        public SharedSupabaseSession(RememberedOperatorLogin login) => this.login = login ?? throw new ArgumentNullException(nameof(login));
        public void Invalidate() => Current = null;
        public async Task<SupabaseAuthSession> GetAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (Current == null || Current.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddSeconds(60))
                    Current = await login.RestoreAsync(cancellationToken);
                return Current;
            }
            finally { gate.Release(); }
        }
        public async Task<SupabaseAuthSession> AuthorizeAsync(string email, string password, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try { return Current = await login.AuthorizeAsync(email, password, cancellationToken); }
            finally { gate.Release(); }
        }
    }
}
