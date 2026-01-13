using System;

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters.Services
{
    /// <summary>
    /// Application-wide notifier for system parameter changes.
    /// Implemented as a singleton so events reach all scopes/connections.
    /// </summary>
    public interface ISystemParametersNotifier
    {
        event EventHandler<SystemParameterChangedEventArgs>? ParameterChanged;
        void NotifyParameterChanged(string? parameterKey, string? newValue);
    }
}