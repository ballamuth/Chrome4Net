using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Diagnostics;
using log4net;
using Chrome4Net.NativeMessaging;

namespace RelayedEcho
{
    class Relay
    {
        private static ILog log = LogManager.GetLogger(typeof(Relay));

        private AnonymousPipeServerStream pipeIn;
        private AnonymousPipeServerStream pipeOut;
        private StreamReader pipeReader;
        private StreamWriter pipeWriter;
        private Process processor;
        private Job job;

        private Port portA;
        private Port portB;
        private List<Exception> portsExceptions;
        private ManualResetEvent stop;

        public Relay(Options options)
        {
            log.Info("creating relay host");

            log.Debug("create interprocess input pipe");
            pipeIn = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            pipeReader = new StreamReader(pipeIn);

            log.Debug("create interprocess output pipe");
            pipeOut = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            pipeWriter = new StreamWriter(pipeOut);

            log.Debug("create processor host");
            processor = new Process();
            processor.StartInfo.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location;
            log.DebugFormat("StartInfo.FileName={0}", processor.StartInfo.FileName);
            processor.StartInfo.Arguments = String.Format("--pipe-in={0} --pipe-out={1} process", pipeOut.GetClientHandleAsString(), pipeIn.GetClientHandleAsString());
            log.DebugFormat("StartInfo.Arguments={0}", processor.StartInfo.Arguments);
            processor.StartInfo.UseShellExecute = false;

            log.Debug("start processor host");
            try
            {
                processor.Start();
            }
            catch (Exception ex)
            {
                log.Fatal("Exception while starting processor host.", ex);
                throw ex;
            }
            log.DebugFormat("processor host process id : {0}", processor.Id);

            log.Debug("join processes into a job so processor host dies together with relay host");
            job = new Job();
            job.AddProcess(Process.GetCurrentProcess().Id);
            job.AddProcess(processor.Id);

            log.Debug("create native messaging ports");
            portA = new Port();
            portB = new Port(pipeIn, pipeOut);
            portsExceptions = new List<Exception>();

            log.Debug("create stop event");
            stop = new ManualResetEvent(false);

            log.Debug("synchronize processes");
            string sync = "SYNC";
            pipeWriter.WriteLine(sync);
            pipeWriter.Flush();
            log.DebugFormat("sent {0}", sync);
            pipeOut.WaitForPipeDrain();
            sync = pipeReader.ReadLine();
            log.DebugFormat("received {0}", sync);

            log.Info("created relay host");
        }

        public void Run()
        {
            log.Info("starting relay host");

            log.Debug("reset stop event and clear ports exceptions");
            stop.Reset();
            portsExceptions = new List<Exception>();

            log.Debug("start asynchronous reading port A");
            portA.BeginRead(delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).ReadPortACallback(_ar); }, this);
            log.Debug("start asynchronous reading port B");
            portB.BeginRead(delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).ReadPortBCallback(_ar); }, this);

            log.Debug("wait for stop event");
            stop.WaitOne();
            log.Debug("stop event");

            log.Debug("check exceptions");
            if (portsExceptions.Count > 0)
            {
                log.Debug("generate aggregated exception");
                throw new AggregateException(portsExceptions);
            }

            log.Info("relay host stopped");
        }
        private void ReadPortACallback(IAsyncResult ar)
        {
            log.Debug("read port A callback");
            try
            {
                string message = portA.EndRead(ar);
                log.DebugFormat("received message\n{0}", message);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        log.Debug("start asynchronous write port B");
                        portB.BeginWrite(message, delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).WritePortBCallback(_ar); }, this);
                    }
                    catch (Exception ex)
                    {
                        log.Error("unhandled exception port B", ex);
                        portsExceptions.Add(ex);
                        stop.Set();
                    }
                }
                else
                {
                    log.Debug("stop signalled");
                }
            }
            catch (EndOfInputStreamException)
            {
                log.Debug("end of native messages stream port A");
                stop.Set();
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port A", ex);
                portsExceptions.Add(ex);
                stop.Set();
            }
        }

        private void WritePortBCallback(IAsyncResult ar)
        {
            log.Debug("write port B callback");
            try
            {
                portB.EndWrite(ar);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        log.Debug("start asynchronous reading port A");
                        portA.BeginRead(delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).ReadPortACallback(_ar); }, this);
                    }
                    catch (EndOfInputStreamException)
                    {
                        log.Debug("end of native messages stream port A");
                        stop.Set();
                    }
                    catch (Exception ex)
                    {
                        log.Error("unhandled exception port A", ex);
                        portsExceptions.Add(ex);
                        stop.Set();
                    }
                }
                else
                {
                    log.Debug("stop signalled");
                }
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port B", ex);
                portsExceptions.Add(ex);
                stop.Set();
            }
        }

        private void ReadPortBCallback(IAsyncResult ar)
        {
            log.Debug("read port B callback");
            try
            {
                string message = portB.EndRead(ar);
                log.DebugFormat("received message\n{0}", message);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        log.Debug("start asynchronous write port A");
                        portA.BeginWrite(message, delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).WritePortACallback(_ar); }, this);
                    }
                    catch (Exception ex)
                    {
                        log.Error("unhandled exception port A", ex);
                        portsExceptions.Add(ex);
                        stop.Set();
                    }
                }
                else
                {
                    log.Debug("stop signalled");
                }
            }
            catch (EndOfInputStreamException)
            {
                log.Debug("end of native messages stream port B");
                stop.Set();
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port B", ex);
                portsExceptions.Add(ex);
                stop.Set();
            }
        }

        private void WritePortACallback(IAsyncResult ar)
        {
            log.Debug("write port A callback");
            try
            {
                portA.EndWrite(ar);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        log.Debug("start asynchronous read port B");
                        portB.BeginRead(delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).ReadPortBCallback(_ar); }, this);
                    }
                    catch (EndOfInputStreamException)
                    {
                        log.Debug("end of native messages stream B");
                        stop.Set();
                    }
                    catch (Exception ex)
                    {
                        log.Error("unhandled exception port B", ex);
                        portsExceptions.Add(ex);
                        stop.Set();
                    }
                }
                else
                {
                    log.Debug("stop signalled");
                }
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port A", ex);
                portsExceptions.Add(ex);
                stop.Set();
            }
        }

        public void Stop()
        {
            stop.Set();
        }
    }
}
