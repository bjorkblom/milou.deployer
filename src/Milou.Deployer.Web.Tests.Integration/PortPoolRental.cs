﻿using System;
using System.Globalization;

namespace Milou.Deployer.Web.Tests.Integration
{
    public sealed class PortPoolRental : IDisposable
    {
        private Action<PortPoolRental> _disposeAction;

        public PortPoolRental(int port, Action<PortPoolRental> disposeAction)
        {
            _disposeAction = disposeAction;
            Port = port;
        }

        public int Port { get; }

        public void Dispose()
        {
            if (_disposeAction is null)
            {
                return;
            }

            _disposeAction?.Invoke(this);

            _disposeAction = null!;
        }

        public override string ToString() => Port.ToString(CultureInfo.InvariantCulture);
    }
}