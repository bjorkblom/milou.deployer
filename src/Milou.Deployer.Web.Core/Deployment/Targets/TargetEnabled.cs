﻿using Arbor.App.Extensions.Messaging;
using MediatR;

namespace Milou.Deployer.Web.Core.Deployment.Targets
{
    public class TargetEnabled : IEvent
    {
        public TargetEnabled(string targetId) => TargetId = targetId;

        public string TargetId { get; }
    }
}