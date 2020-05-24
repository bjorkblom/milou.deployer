﻿using Arbor.App.Extensions.Messaging;
using MediatR;


namespace Milou.Deployer.Web.Core.Agents.Pools
{
    public class GetAgentPoolsQuery : IQuery<AgentPoolListResult>
    {
    }
}