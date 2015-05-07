using System;
using System.Runtime.Serialization;

namespace TwainScan
{
    [System.Serializable]
    public class TwainScanException : System.Exception
    {
        public TwainScanException() : base("TwainScan exception.") { }
        public TwainScanException(string message) : base(message) { }
        public TwainScanException(string message, Exception innerException) : base(message, innerException) { }
        protected TwainScanException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
