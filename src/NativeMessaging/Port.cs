using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace Chrome4Net.NativeMessaging
{
    public class Port
    {
        private Stream istream;
        private Stream ostream;

        public Port()
        {
            istream = Console.OpenStandardInput();
            ostream = Console.OpenStandardOutput();
        }

        public Port(Stream istream, Stream ostream)
        {
            if (istream == null) throw new ArgumentNullException("Argument 'istream' must be not null.");
            if (ostream == null) throw new ArgumentNullException("Argument 'ostream' must be not null.");
            this.istream = istream;
            this.ostream = ostream;
        }

        public IAsyncResult BeginRead(AsyncCallback callback, object state)
        {
            AsyncResult ar =  new AsyncResult(this, callback, state);
            try { ar.waitTimeout = istream.ReadTimeout; }
            catch (InvalidOperationException) { ar.waitTimeout = System.Threading.Timeout.Infinite; }
            ar.lengthBuffer = new byte[4];
            ar.lengthOffset = 0;
            istream.BeginRead(
                ar.lengthBuffer,
                ar.lengthOffset,
                ar.lengthBuffer.Length - ar.lengthOffset,
                delegate(IAsyncResult _ar) { ((AsyncResult)_ar.AsyncState).port.ReadLengthCallback((AsyncResult)_ar.AsyncState, _ar); },
                ar);
            return ar;
        }

        private void ReadLengthCallback(AsyncResult ar, IAsyncResult lengthAsyncResult)
        {
            try
            {
                Debug.Assert(lengthAsyncResult.IsCompleted == true);
                ar.lengthIsCompleted = lengthAsyncResult.IsCompleted;
                ar.lengthCompletedSynchronously = lengthAsyncResult.CompletedSynchronously;
                int bytesRead = istream.EndRead(lengthAsyncResult);
                Debug.Assert((0 <= bytesRead) && (bytesRead <= ar.lengthBuffer.Length));
                if (bytesRead == 0) throw new EndOfInputStreamException();
                if (bytesRead < ar.lengthBuffer.Length)
                {
                    ar.lengthOffset += bytesRead;
                    istream.BeginRead(
                        ar.lengthBuffer,
                        ar.lengthOffset,
                        ar.lengthBuffer.Length - ar.lengthOffset,
                        delegate(IAsyncResult _ar) { ((AsyncResult)_ar.AsyncState).port.ReadLengthCallback((AsyncResult)_ar.AsyncState, _ar); },
                        ar);
                    return;
                }
                int messageLength = System.BitConverter.ToInt32(ar.lengthBuffer, 0);
                if (messageLength <= 0) throw new ProtocolErrorException(string.Format("Read zero or negative input message length : {0}", messageLength));
                ar.messageBuffer = new byte[messageLength];
                ar.messageOffset = 0;
                istream.BeginRead(
                    ar.messageBuffer,
                    ar.messageOffset,
                    ar.messageBuffer.Length - ar.messageOffset,
                    delegate(IAsyncResult _ar) { ((AsyncResult)_ar.AsyncState).port.ReadMessageCallback((AsyncResult)_ar.AsyncState, _ar); },
                    ar);
            }
            catch (Exception ex)
            {
                ar.lengthException = ex;
                ar.wait.Set();
                if (ar.callback != null) ar.callback(ar);
            }
        }

        private void ReadMessageCallback(AsyncResult ar, IAsyncResult messageAsyncResult)
        {
            try
            {
                Debug.Assert(messageAsyncResult.IsCompleted == true);
                ar.messageIsCompleted = messageAsyncResult.IsCompleted;
                ar.messageCompletedSynchronously = messageAsyncResult.CompletedSynchronously;
                int bytesRead = istream.EndRead(messageAsyncResult);
                Debug.Assert((0 <= bytesRead) && (bytesRead <= ar.messageBuffer.Length));
                if (bytesRead == 0) throw new ProtocolErrorException("Unexpected end of input stream.");
                if (bytesRead < ar.messageBuffer.Length)
                {
                    ar.messageOffset += bytesRead;
                    istream.BeginRead(
                        ar.messageBuffer,
                        ar.messageOffset,
                        ar.messageBuffer.Length - ar.messageOffset,
                        delegate(IAsyncResult _ar) { ((AsyncResult)_ar.AsyncState).port.ReadMessageCallback((AsyncResult)_ar.AsyncState, _ar); },
                        ar);
                    return;
                }
                ar.wait.Set();
                if (ar.callback != null) ar.callback(ar);
            }
            catch (Exception ex)
            {
                ar.lengthException = ex;
                ar.wait.Set();
                if (ar.callback != null) ar.callback(ar);
            }
        }

        public string EndReadString(IAsyncResult asyncResult)
        {
            if (asyncResult == null) throw new ArgumentNullException("Argument 'asyncResult' must be not null.");
            if (!typeof(AsyncResult).IsInstanceOfType(asyncResult)) throw new ArgumentException(string.Format("Argument 'asyncResult' must be instance of {0}", typeof(AsyncResult)));

            AsyncResult ar = (AsyncResult)asyncResult;
            if (ar.wait.WaitOne(ar.waitTimeout))
            {
                if (ar.lengthException != null) throw ar.lengthException;
                if (ar.messageException != null) throw ar.messageException;

                string message;
                try
                {
                    message = System.Text.Encoding.UTF8.GetString(ar.messageBuffer);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new ProtocolErrorException("Invalid input message encoding.", ex);
                }
                return message;
            }
            else
            {
                throw new TimeoutException();
            }
        }

        public IAsyncResult BeginWrite(string message, AsyncCallback callback, object state)
        {
            AsyncResult ar = new AsyncResult(this, callback, state);
            try { ar.waitTimeout = ostream.WriteTimeout; }
            catch (InvalidOperationException) { ar.waitTimeout = System.Threading.Timeout.Infinite; }
            try
            {
                ar.messageBuffer = System.Text.Encoding.UTF8.GetBytes(message);
                ar.messageOffset = 0;
            }
            catch (EncoderFallbackException ex)
            {
                throw new ProtocolErrorException("Invalid output message encoding.", ex);
            }
            ar.lengthBuffer = System.BitConverter.GetBytes((Int32)ar.messageBuffer.Length);
            ar.lengthOffset = 0;
            Debug.Assert(ar.lengthBuffer.Length == 4);
            ostream.BeginWrite(
                ar.lengthBuffer,
                ar.lengthOffset,
                ar.lengthBuffer.Length - ar.lengthOffset,
                delegate(IAsyncResult _ar) { ((AsyncResult)_ar.AsyncState).port.WriteLengthCallback((AsyncResult)_ar.AsyncState, _ar); },
                ar);
            return ar;
        }

        private void WriteLengthCallback(AsyncResult ar, IAsyncResult lengthAsyncResult)
        {
            try
            {
                Debug.Assert(lengthAsyncResult.IsCompleted == true);
                ar.lengthIsCompleted = lengthAsyncResult.IsCompleted;
                ar.lengthCompletedSynchronously = lengthAsyncResult.CompletedSynchronously;
                ostream.EndWrite(lengthAsyncResult);
                ostream.BeginWrite(
                    ar.messageBuffer,
                    ar.messageOffset,
                    ar.messageBuffer.Length - ar.messageOffset,
                    delegate(IAsyncResult _ar) { ((AsyncResult)_ar.AsyncState).port.WriteMessageCallback((AsyncResult)_ar.AsyncState, _ar); },
                ar);
            }
            catch (Exception ex)
            {
                ar.lengthException = ex;
                ar.wait.Set();
                if (ar.callback != null) ar.callback(ar);
            }
        }

        private void WriteMessageCallback(AsyncResult ar, IAsyncResult messageAsyncResult)
        {
            try
            {
                Debug.Assert(messageAsyncResult.IsCompleted == true);
                ar.messageIsCompleted = messageAsyncResult.IsCompleted;
                ar.messageCompletedSynchronously = messageAsyncResult.CompletedSynchronously;
                ostream.EndWrite(messageAsyncResult);
                ar.wait.Set();
                if (ar.callback != null) ar.callback(ar);
            }
            catch (Exception ex)
            {
                ar.messageException = ex;
                ar.wait.Set();
                if (ar.callback != null) ar.callback(ar);
            }
        }

        public void EndWrite(IAsyncResult asyncResult)
        {
            if (asyncResult == null) throw new ArgumentNullException("Argument 'asyncResult' must be not null.");
            if (!typeof(AsyncResult).IsInstanceOfType(asyncResult)) throw new ArgumentException(string.Format("Argument 'asyncResult' must be instance of {0}", typeof(AsyncResult)));

            AsyncResult ar = (AsyncResult)asyncResult;
            if (ar.wait.WaitOne(ar.waitTimeout))
            {
                if (ar.lengthException != null) throw ar.lengthException;
                if (ar.messageException != null) throw ar.messageException;
            }
            else
            {
                throw new TimeoutException();
            }
        }

        public string ReadString()
        {
            return EndReadString(BeginRead(null, null));
        }

        public void Write(string message)
        {
            EndWrite(BeginWrite(message, null, null));
        }
    }
}
