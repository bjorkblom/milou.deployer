﻿using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Arbor.App.Extensions.Time;
using JetBrains.Annotations;
using Serilog;

namespace Milou.Deployer.Web.Core.Agents
{
    [UsedImplicitly]
    public class AgentsData
    {
        private readonly ConcurrentDictionary<string, AgentState> _agents =
            new ConcurrentDictionary<string, AgentState>();

        private readonly ICustomClock _customClock;
        private readonly ILogger _logger;

        public AgentsData(ICustomClock customClock, ILogger logger)
        {
            _customClock = customClock;
            _logger = logger;
        }

        public ImmutableArray<AgentInfo> Agents => _agents.Select(agent => new AgentInfo(agent.Key, agent.Value.ConnectedAt, agent.Value.ConnectionId, agent.Value.CurrentDeploymentTaskId)).ToImmutableArray();

        public void AgentConnected(AgentConnected agentConnected)
        {
            var agentId = agentConnected.AgentId;

            if (!_agents.ContainsKey(agentId))
            {
                _agents.TryAdd(agentId,
                    new AgentState(agentId)
                    {
                        ConnectedAt = _customClock.UtcNow(), IsConnected = true,
                        ConnectionId = agentConnected.ConnectionId
                    });
            }
            else
            {
                if (_agents.TryGetValue(agentId, out var state))
                {
                    state.IsConnected = true;
                    state.ConnectedAt = _customClock.UtcNow();
                    state.ConnectionId = agentConnected.ConnectionId;
                }
                else
                {
                    _logger.Error("Could not get agent state for agent id {AgentId}", agentId);
                }
            }
        }

        public void AgentAssigned(string agentId, string deploymentTaskId)
        {
            if (!_agents.TryGetValue(agentId, out var state))
            {
               throw new InvalidOperationException($"The agent {agentId} could not be found");
            }

            state.CurrentDeploymentTaskId = deploymentTaskId;
        }

        public void AgentDone(string agentId)
        {
            if (!_agents.TryGetValue(agentId, out var state))
            {
                throw new InvalidOperationException($"The agent {agentId} could not be found");
            }

            state.CurrentDeploymentTaskId = default;
        }
    }
}