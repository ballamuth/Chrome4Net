using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Chrome4Net.NativeMessaging
{
    [System.Serializable]
    public class NativeMessagingException : System.Exception
    {
        public NativeMessagingException() { }
        public NativeMessagingException(string message) : base(message) { }
        public NativeMessagingException(string message, Exception innerException) : base(message, innerException) { }
        protected NativeMessagingException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

}
