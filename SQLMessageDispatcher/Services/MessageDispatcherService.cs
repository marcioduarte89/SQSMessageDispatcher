﻿using Amazon.Runtime.Internal;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using SQLMessageDispatcher.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SQLMessageDispatcher.Services
{
    public class MessageDispatcherService : IMessageDispatcherService
    {
        private readonly IWorkersManager _workersManager;
        private readonly IAmazonSQS _amazonSqs;
        private readonly ReceiveMessageRequest _receiveMessageRequest;
        private readonly ILogger<MessageDispatcherService> _logger;
        private static EventWaitHandle _workersReadyHandle = new AutoResetEvent(false);

        public MessageDispatcherService(
            IWorkersManager workersManager, 
            IAmazonSQS amazonSqs, 
            ReceiveMessageRequest receiveMessageRequest, 
            ILogger<MessageDispatcherService> logger)
        {
            _workersManager = workersManager;
            _amazonSqs = amazonSqs;
            _receiveMessageRequest = receiveMessageRequest;
            _logger = logger;
            _workersManager.ReadyToWork += WorkersManager_ReadyToWork;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Waits until at least one of the worker threads is ready so we can start processing more messages or after 20 seconds (to avoid deadlock in case of all workers threads waiting already)
                _workersReadyHandle.WaitOne(TimeSpan.FromSeconds(20));

                try
                {
                    var resultMessage = await _amazonSqs.ReceiveMessageAsync(_receiveMessageRequest, cancellationToken);
                    _workersManager.AddWork(resultMessage.Messages);
                }
                catch (HttpErrorResponseException ex)
                {
                    _logger.LogError(ex, "Http Error has ocurred receiving messages from the queue");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception has occurred when processing messages from SQS Queue");
                }
            }

            _workersManager.FinishWork();

            // awaits for a period of time hoping all threads finish exist gracefully
            Thread.Sleep(TimeSpan.FromSeconds(20));
            _logger.LogInformation("Main thread existing");
        }

        private void WorkersManager_ReadyToWork(object sender, EventArgs e)
        {
            _workersReadyHandle.Set();
        }
    }
}
