using System;
using Microsoft.Extensions.Logging;

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters.Services
{
    public class SystemParametersNotifier : ISystemParametersNotifier
    {
        private readonly ILogger<SystemParametersNotifier> _logger;

        public SystemParametersNotifier(ILogger<SystemParametersNotifier> logger)
        {
            _logger = logger;
        }

        public event EventHandler<SystemParameterChangedEventArgs>? ParameterChanged;

        public void NotifyParameterChanged(string? parameterKey, string? newValue)
        {
            try
            {
                ParameterChanged?.Invoke(this, new SystemParameterChangedEventArgs(parameterKey, newValue));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while notifying parameter change for {ParameterKey}", parameterKey);
            }
        }
    }
}