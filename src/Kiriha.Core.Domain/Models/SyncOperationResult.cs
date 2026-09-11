using System;

namespace Kiriha.Core.Domain.Models;

public record class SyncSuccess(int UpdatedCount, string? Message = null);

public record class SyncRateLimited(TimeSpan RetryAfter);

public record class SyncAuthExpired(string ServiceName);

public record class SyncNetworkError(string ErrorMessage);

/// <summary>
/// First-class C# 15 discriminated union representing the outcome of a synchronization operation.
/// Provides exhaustive pattern matching across all sync outcomes.
/// </summary>
public union SyncOperationResult(SyncSuccess, SyncRateLimited, SyncAuthExpired, SyncNetworkError);
