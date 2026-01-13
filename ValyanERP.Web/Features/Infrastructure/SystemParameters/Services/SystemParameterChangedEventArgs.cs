using System;

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters.Services
{
    /// <summary>
    /// Event args for parameter change notifications.
    /// </summary>
    public class SystemParameterChangedEventArgs : EventArgs
    {
        public string? ParameterKey { get; }
        public string? NewValue { get; }

        public SystemParameterChangedEventArgs(string? parameterKey, string? newValue)
        {
            ParameterKey = parameterKey;
            NewValue = newValue;
        }
    }
}