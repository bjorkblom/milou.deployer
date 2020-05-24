﻿using Arbor.App.Extensions.Messaging;
using MediatR;

namespace Milou.Deployer.Web.Core.Deployment
{
    public class WorkerCreated : IEvent
    {
        public WorkerCreated(IDeploymentTargetWorker worker) => Worker = worker;
        public IDeploymentTargetWorker Worker { get; }
    }
}