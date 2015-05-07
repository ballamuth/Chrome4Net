using System;
using System.Runtime.Serialization;

namespace TwainScan
{
    [System.Serializable]
    public class SynchronizationException : TwainScanException
    {
        public SynchronizationException() : base("Synchronization failed.") { }
        public SynchronizationException(string message) : base(message) { }
        public SynchronizationException(string message, Exception innerException) : base(message, innerException) { }
        protected SynchronizationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
