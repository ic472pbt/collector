using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace MapOutToTheCloud.classes
{
    public class ServiceBaseLifeTime : ServiceBase, IHostLifetime
    {
        private readonly TaskCompletionSource<object> _delayStart;
        private IHostApplicationLifetime ApplicationLifetime { get; }
        public ServiceBaseLifeTime(IHostApplicationLifetime
        applicationLifetime)
        {
            _delayStart = new TaskCompletionSource<object>();
            ApplicationLifetime = applicationLifetime ?? throw new
            ArgumentNullException(nameof(applicationLifetime));
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if(OperatingSystem.IsWindows())
            Stop();
            return Task.CompletedTask;
        }
        public Task WaitForStartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => _delayStart.TrySetCanceled());
            if(OperatingSystem.IsWindows())
            ApplicationLifetime.ApplicationStopping.Register(Stop);
            // Otherwise this would block and prevent IHost.StartAsync from finishing. 
            new Thread(Run).Start();
            return _delayStart.Task;
        }
        private void Run()
        {
            try
            {
            if(OperatingSystem.IsWindows())
                Run(this); // This blocks until the service is stopped.
                _delayStart.TrySetException(new
                 InvalidOperationException("Stopped without starting"));
            }
            catch (Exception ex)
            {
                _delayStart.TrySetException(ex);
            }
        }
        // Called by base.Run when the service is ready to start.
        protected override void OnStart(string[] args)
        {
            _delayStart.TrySetResult(null);
            if(OperatingSystem.IsWindows())
            base.OnStart(args);
        }
        // Called by base.Stop. This may be called multiple times by  service Stop, ApplicationStopping, and StopAsync.
        // That's OK because StopApplication uses a CancellationTokenSource and prevents any recursion.
        protected override void OnStop()
        {
            ApplicationLifetime.StopApplication();
            if(OperatingSystem.IsWindows())
                base.OnStop();
        }
        protected override void OnPause()
        {
            // Custom action on pause
            if(OperatingSystem.IsWindows())
            base.OnPause();
        }
        protected override void OnContinue()
        {
            // Custom action on continue
            if(OperatingSystem.IsWindows())
            base.OnContinue();
        }
    }
}
