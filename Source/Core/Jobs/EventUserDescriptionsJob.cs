﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Queues.Models;
using Exceptionless.Core.Repositories;
using Exceptionless.Core.Repositories.Base;
using Exceptionless.Core.Models.Data;
using Foundatio.Jobs;
using Foundatio.Metrics;
using Foundatio.Queues;
using NLog.Fluent;
#pragma warning disable 1998

namespace Exceptionless.Core.Jobs {
    public class EventUserDescriptionsJob : QueueProcessorJobBase<EventUserDescription> {
        private readonly IEventRepository _eventRepository;
        private readonly IMetricsClient _metricsClient;

        public EventUserDescriptionsJob(IQueue<EventUserDescription> queue, IEventRepository eventRepository, IMetricsClient metricsClient) : base(queue) {
            _eventRepository = eventRepository;
            _metricsClient = metricsClient;
        }

        protected override async Task<JobResult> ProcessQueueItemAsync(QueueEntry<EventUserDescription> queueEntry, CancellationToken cancellationToken = default(CancellationToken)) {
            Log.Trace().Message("Processing user description: id={0}", queueEntry.Id).Write();

            try {
                await ProcessUserDescriptionAsync(queueEntry.Value).AnyContext();
                Log.Info().Message("Processed user description: id={0}", queueEntry.Id).Write();
            } catch (DocumentNotFoundException ex){
                Log.Error().Exception(ex).Message("An event with this reference id \"{0}\" has not been processed yet or was deleted. Queue Id: {1}", ex.Id, queueEntry.Id).Write();
                return JobResult.FromException(ex);
            } catch (Exception ex) {
                Log.Error().Exception(ex).Message("An error occurred while processing the EventUserDescription '{0}': {1}", queueEntry.Id, ex.Message).Write();
                return JobResult.FromException(ex);
            }

            return JobResult.Success;
        }
        
        private async Task ProcessUserDescriptionAsync(EventUserDescription description) {
            var ev = (await _eventRepository.GetByReferenceIdAsync(description.ProjectId, description.ReferenceId).AnyContext()).Documents.FirstOrDefault();
            if (ev == null)
                throw new DocumentNotFoundException(description.ReferenceId);

            var ud = new UserDescription {
                EmailAddress = description.EmailAddress,
                Description = description.Description
            };

            if (description.Data.Count > 0)
                ev.Data.AddRange(description.Data);

            ev.SetUserDescription(ud);

            await _eventRepository.SaveAsync(ev).AnyContext();
        }
    }
}