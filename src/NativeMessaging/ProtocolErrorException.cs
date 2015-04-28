using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Chrome4Net.NativeMessaging
{
    [System.Serializable]
    public class ProtocolErrorException : NativeMessagingException
    {
        public ProtocolErrorException() : base("Native messaging protocol error.") { }
        public ProtocolErrorException(string message) : base(message) { }
        public ProtocolErrorException(string message, Exception innerException) : base(message, innerException) { }
        protected ProtocolErrorException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
