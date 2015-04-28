using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

namespace Chrome4Net.NativeMessaging
{
    class AsyncResult : IAsyncResult
    {
        public object AsyncState { get { return state; } }
        public WaitHandle AsyncWaitHandle { get { return wait; } }
        public bool CompletedSynchronously { get { return lengthCompletedSynchronously && messageCompletedSynchronously; } }
        public bool IsCompleted { get { return lengthIsCompleted && messageIsCompleted; } }

        public Port port { get; private set; }
        public AsyncCallback callback { get; private set; }
        public object state { get; private set; }
        public ManualResetEvent wait { get; private set; }
        public int waitTimeout;

        public bool lengthIsCompleted;
        public bool lengthCompletedSynchronously;
        public byte[] lengthBuffer;
        public int lengthOffset;
        public Exception lengthException;

        public bool messageIsCompleted;
        public bool messageCompletedSynchronously;
        public byte[] messageBuffer;
        public int messageOffset;
        public Exception messageException;

        public AsyncResult(Port port, AsyncCallback callback, object state)
        {
            this.port = port;
            this.callback = callback;
            this.state = state;
            wait = new ManualResetEvent(false);
            waitTimeout = System.Threading.Timeout.Infinite;

            lengthIsCompleted = false;
            lengthCompletedSynchronously = false;
            lengthBuffer = null;
            lengthOffset = 0;
            lengthException = null;

            messageIsCompleted = false;
            messageCompletedSynchronously = false;
            messageBuffer = null;
            messageOffset = 0;
            messageException = null;
        }
    }
}
