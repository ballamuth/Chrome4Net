using System;
using System.Runtime.Serialization;

namespace TwainScan
{
    [System.Serializable]
    public class ScanSettingsException: TwainScanException
    {
        public ScanSettingsException() : base("Invalid scan settings.") { }
        public ScanSettingsException(string message) : base(message) { }
        public ScanSettingsException(string message, Exception innerException) : base(message, innerException) { }
        protected ScanSettingsException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
