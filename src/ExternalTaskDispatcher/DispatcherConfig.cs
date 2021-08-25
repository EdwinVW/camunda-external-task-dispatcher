using System;
using System.Collections.Generic;

namespace ExternalTaskDispatcher
{
    public class DispatcherConfig
    {
        public string Id { get; set; }
        public string CamundaUrl { get; set; }
        public string APIMUrl { get; set; }
        public string APIMKey { get; set; }
        public string Topics { get; set; }
        public bool AutomaticTopicDiscovery { get; set; }
        public long LongPollingIntervalInMs { get; set; }
        public long TaskLockDurationInMs { get; set; }
        public long TopicCacheInvalidationIntervalInMin { get; set; }
    }
}