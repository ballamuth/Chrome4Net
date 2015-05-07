using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Diagnostics;
using log4net;
using Chrome4Net.NativeMessaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwainScan
{
    /// <summary>
    /// Native messages relay class.
    /// </summary>
    class Relay
    {
        private static ILog log = LogManager.GetLogger(typeof(Relay));

        private AnonymousPipeServerStream pipeIn;
        private AnonymousPipeServerStream pipeOut;
        private Process processor;
        private Job job;
        private ManualResetEvent sync;

        private Port portA;
        private Port portB;
        private List<Exception> portsExceptions;
        private ManualResetEvent stop;

        public Relay(Program.Options options)
        {
            log.Debug("creating relay host");

            log.Debug("create interprocess input pipe");
            pipeIn = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            log.Debug("create interprocess output pipe");
            pipeOut = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);

            log.Debug("create sync event");
            sync = new ManualResetEvent(false);

            log.Debug("create processor host");
            processor = new Process();
            processor.StartInfo.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location;
            log.DebugFormat("StartInfo.FileName={0}", processor.StartInfo.FileName);
            processor.StartInfo.Arguments = String.Format("--pipe-in={0} --pipe-out={1} process", pipeOut.GetClientHandleAsString(), pipeIn.GetClientHandleAsString());
            log.DebugFormat("StartInfo.Arguments={0}", processor.StartInfo.Arguments);
            processor.StartInfo.UseShellExecute = false;
            //// processor.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;  ???? do we really need this ????

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

            log.Debug("relay host created");
        }

        public Relay Sync(int timeout = Timeout.Infinite)
        {
            log.Debug("synchronize processes");

            log.Debug("clear stop and sync events and clear ports exceptions");
            stop.Reset();
            sync.Reset();
            portsExceptions = new List<Exception>();

            string message = new JObject(new JProperty("sync", true)).ToString(Formatting.None);
            log.DebugFormat("sync message\n{0}", message);

            log.Debug("begin asynchronous write port B");
            portB.BeginWrite(message, delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).SyncWriteCallback(_ar); }, this);

            log.DebugFormat("wait for stop event (timeout={0})", timeout);
            if (!stop.WaitOne(timeout)) throw new TimeoutException("Processes synchronization timeout.");
            log.Debug("stop event");

            log.Debug("check ports exceptions");
            if (portsExceptions.Count > 0)
            {
                log.Debug("generate aggregated exception");
                throw new AggregateException(portsExceptions);
            }

            log.Debug("check sync event");
            if (!sync.WaitOne(0)) throw new SynchronizationException();

            log.Debug("processes are synchronized");
            return this;
        }

        private void SyncWriteCallback(IAsyncResult ar)
        {
            try
            {
                log.Debug("end asynchronous write port B");
                portB.EndWrite(ar);

                log.Debug("begin asynchronous read port B");
                portB.BeginRead(delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).SyncReadCallback(_ar); }, this);
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port B", ex);
                portsExceptions.Add(ex);
                stop.Set();
            }
        }

        private void SyncReadCallback(IAsyncResult ar)
        {
            try
            {
                log.Debug("end asynchronous read port B");
                string message = portB.EndRead(ar);

                log.DebugFormat("received message\n{0}", message);

                log.Debug("set stop and sync events");
                sync.Set();
                stop.Set();
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port B", ex);
                portsExceptions.Add(ex);
                stop.Set();
            }
        }

        public Relay Run()
        {
            log.Debug("relay host started");

            log.Debug("clear stop event and clear ports exceptions");
            stop.Reset();
            portsExceptions = new List<Exception>();

            log.Debug("begin asynchronous read port A");
            portA.BeginRead(delegate(IAsyncResult _ar) { ((Relay)_ar.AsyncState).ReadPortACallback(_ar); }, this);
            log.Debug("begin asynchronous read port B");
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

            log.Debug("relay host stopped");
            return this;
        }

        private void ReadPortACallback(IAsyncResult ar)
        {
            log.Debug("end asynchronous read port A");
            try
            {
                string message = portA.EndRead(ar);
                log.DebugFormat("received message\n{0}", message);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        log.Debug("begin asynchronous write port B");
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
            log.Debug("end asynchronous write port B");
            try
            {
                portB.EndWrite(ar);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        log.Debug("begin asynchronous read port A");
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
            log.Debug("end asynchronous read port B");
            try
            {
                string message = portB.EndRead(ar);
                log.DebugFormat("received message\n{0}", message);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        log.Debug("begin asynchronous write port A");
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
            log.Debug("end asynchronous write port A");
            try
            {
                portA.EndWrite(ar);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        log.Debug("begin asynchronous read port B");
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

        public Relay Stop()
        {
            log.Debug("stop relay host");
            stop.Set();
            return this;
        }

    }
}
