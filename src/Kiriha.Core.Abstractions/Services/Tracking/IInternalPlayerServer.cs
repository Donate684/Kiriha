using System;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Core.Abstractions.Services;

public interface IInternalPlayerServer
{
    event EventHandler<InternalPlayerState>? PlayerStateChanged;
}
