﻿// Copyright 2013-2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using Serilog.Core.Enrichers;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parameters;

namespace Serilog.Core.Pipeline
{
    sealed class Logger : ILogger, ILogEventSink, IDisposable
    {
        readonly MessageTemplateProcessor _messageTemplateProcessor;
        readonly ILogEventSink _sink;
        readonly Action _dispose;
        readonly ILogEventEnricher[] _enrichers;

        // It's important that checking minimum level is a very
        // quick (CPU-cacheable) read in the simple case, hence
        // we keep a separate field from the switch, which may
        // not be specified. If it is, we'll set _minimumLevel
        // to its lower limit and fall through to the secondary check.
        readonly LogEventLevel _minimumLevel;
        readonly LoggingLevelSwitch _levelSwitch;

        public Logger(
            MessageTemplateProcessor messageTemplateProcessor,
            LogEventLevel minimumLevel,
            ILogEventSink sink,
            IEnumerable<ILogEventEnricher> enrichers,
            Action dispose = null)
            : this(messageTemplateProcessor, minimumLevel, sink, enrichers, dispose, null)
        {
        }

        public Logger(
            MessageTemplateProcessor messageTemplateProcessor,
            LoggingLevelSwitch levelSwitch,
            ILogEventSink sink,
            IEnumerable<ILogEventEnricher> enrichers,
            Action dispose = null)
            : this(messageTemplateProcessor, LevelAlias.Minimum, sink, enrichers, dispose, levelSwitch)
        {
        }

        Logger(
            MessageTemplateProcessor messageTemplateProcessor,
            LogEventLevel minimumLevel,
            ILogEventSink sink,
            IEnumerable<ILogEventEnricher> enrichers,
            Action dispose = null,
            LoggingLevelSwitch levelSwitch = null)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (enrichers == null) throw new ArgumentNullException(nameof(enrichers));

            _messageTemplateProcessor = messageTemplateProcessor;
            _minimumLevel = minimumLevel;
            _sink = sink;
            _dispose = dispose;
            _levelSwitch = levelSwitch;
            _enrichers = enrichers.ToArray();
        }

        public ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers)
        {
            return new Logger(
                _messageTemplateProcessor,
                _minimumLevel,
                this,
                (enrichers ?? new ILogEventEnricher[0]).ToArray(),
                null,
                _levelSwitch);
        }

        public ILogger ForContext(string propertyName, object value, bool destructureObjects = false)
        {
            return ForContext(new[] {
                new FixedPropertyEnricher(
                    _messageTemplateProcessor.CreateProperty(propertyName, value, destructureObjects)) });
        }

        public ILogger ForContext(Type source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return ForContext(Constants.SourceContextPropertyName, source.FullName);
        }

        public ILogger ForContext<TSource>()
        {
            return ForContext(typeof(TSource));
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Write(LogEventLevel level, string messageTemplate, params object[] propertyValues)
        {
            Write(level, null, messageTemplate, propertyValues);
        }

        public bool IsEnabled(LogEventLevel level)
        {
            if ((int)level < (int)_minimumLevel)
                return false;

            return _levelSwitch == null ||
                (int)level >= (int)_levelSwitch.MinimumLevel;
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Write(LogEventLevel level, Exception exception, string messageTemplate, params object[] propertyValues)
        {
            if (messageTemplate == null) return;
            if (!IsEnabled(level)) return;

            // Catch a common pitfall when a single non-object array is cast to object[]
            if (propertyValues != null &&
                propertyValues.GetType() != typeof(object[]))
                propertyValues = new object[] { propertyValues };

            var now = DateTimeOffset.Now;

            MessageTemplate parsedTemplate;
            IEnumerable<LogEventProperty> properties;
            _messageTemplateProcessor.Process(messageTemplate, propertyValues, out parsedTemplate, out properties);

            var logEvent = new LogEvent(now, level, exception, parsedTemplate, properties);
            Dispatch(logEvent);
        }

        public void Write(LogEvent logEvent)
        {
            if (logEvent == null) return;
            if (!IsEnabled(logEvent.Level)) return;
            Dispatch(logEvent);
        }

        void ILogEventSink.Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));
            Write(logEvent);
        }

        void Dispatch(LogEvent logEvent)
        {
            foreach (var enricher in _enrichers)
            {
                try
                {
                    enricher.Enrich(logEvent, _messageTemplateProcessor);
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Exception {0} caught while enriching {1} with {2}.", ex, logEvent, enricher);
                }
            }

            _sink.Emit(logEvent);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Verbose(string messageTemplate, params object[] propertyValues)
        {
            Verbose(null, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Verbose(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            Write(LogEventLevel.Verbose, exception, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Debug(string messageTemplate, params object[] propertyValues)
        {
            Debug(null, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Debug(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            Write(LogEventLevel.Debug, exception, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Information(string messageTemplate, params object[] propertyValues)
        {
            Information(null, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Information(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            Write(LogEventLevel.Information, exception, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Warning(string messageTemplate, params object[] propertyValues)
        {
            Warning(null, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Warning(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            Write(LogEventLevel.Warning, exception, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Error(string messageTemplate, params object[] propertyValues)
        {
            Error(null, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Error(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            Write(LogEventLevel.Error, exception, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Fatal(string messageTemplate, params object[] propertyValues)
        {
            Fatal(null, messageTemplate, propertyValues);
        }

        [MessageTemplateFormatMethod("messageTemplate")]
        public void Fatal(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            Write(LogEventLevel.Fatal, exception, messageTemplate, propertyValues);
        }

        public void Dispose()
        {
            _dispose?.Invoke();
        }
    }
}
