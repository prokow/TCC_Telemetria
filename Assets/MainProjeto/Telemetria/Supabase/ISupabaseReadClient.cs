using System;
using System.Threading;
using System.Threading.Tasks;

namespace MainProjeto.Telemetria.Supabase
{
    public enum SupabaseFailure
    {
        Connection,
        AccessDenied,
        Http,
        InvalidResponse
    }

    public sealed class SupabaseRequestException : Exception
    {
        public SupabaseFailure Failure { get; }
        public long StatusCode { get; }

        public SupabaseRequestException(SupabaseFailure failure, string message, long statusCode = 0)
            : base(message)
        {
            Failure = failure;
            StatusCode = statusCode;
        }
    }

    public interface ISupabaseReadClient
    {
        Task<string> GetAsync(string resourceAndQuery, CancellationToken cancellationToken);
    }
}
