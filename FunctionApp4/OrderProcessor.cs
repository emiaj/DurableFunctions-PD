using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace FunctionApp4
{
    public static class OrderProcessor
    {
        [FunctionName("OrderProcessor")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var order = context.GetInput<string>();
            var messages = new List<string>();

            context.SetCustomStatus("Validating credit card...");
            var creditCardValidationSucceed = await context.CallActivityAsync<bool>(
                functionName: "OrderCreditCardValidator_Validate",
                input: order);
            context.SetCustomStatus("Finished validating credit card");

            if (creditCardValidationSucceed)
            {
                messages.Add("Credit card validation succeeded");

                context.SetCustomStatus("Processing payment...");
                var paymentSucceed = await context.CallActivityWithRetryAsync<bool>(
                    functionName: "OrderPaymentProcessor_Process",
                    retryOptions: new RetryOptions(firstRetryInterval: TimeSpan.FromSeconds(5), maxNumberOfAttempts: 5),
                    input: order);
                context.SetCustomStatus("Finished processing payment");


                if (paymentSucceed)
                {
                    messages.Add("Payment processed successfully");

                    var notifyPayment = context.CallActivityWithRetryAsync<bool>(
                        functionName: "PaymentNotifier_Notify",
                        retryOptions: new RetryOptions(firstRetryInterval: TimeSpan.FromSeconds(5), maxNumberOfAttempts: 5),
                        input: order);

                    var dispatchOrder = context.CallActivityWithRetryAsync<bool>(
                        functionName: "OrderDispatcher_Dispatch",
                        retryOptions: new RetryOptions(firstRetryInterval: TimeSpan.FromSeconds(5), maxNumberOfAttempts: 5),
                        input: order);


                    context.SetCustomStatus("Notifying customer and dispatching order");
                    await Task.WhenAll(notifyPayment, dispatchOrder);


                    if (await notifyPayment == false)
                    {
                        messages.Add($"There was a problem notifying the payment for order #${order}, call 555-5555-5555 and quote your order number");
                    }
                    else
                    {
                        messages.Add("Payment notification submitted to customer");
                    }

                    if (await dispatchOrder == false)
                    {
                        messages.Add($"There was a problem dispatching the products for order #${order}, call 555-5555-5555 and quote your order number");
                    }
                    else
                    {
                        messages.Add("Products have notified for dispatching successfully");
                    }

                    context.SetCustomStatus("Order processing completed");
                }
                else
                {
                    messages.Add("Payment processing failure");
                }

            }
            else
            {
                messages.Add("Credit card validation failed");
            }

            return messages;
        }

        [FunctionName("OrderProcessor_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "OrderProcessor_HttpStart/{order}"
            )]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            string order,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("OrderProcessor", order);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }

    public static class OrderCreditCardValidator
    {
        [FunctionName("OrderCreditCardValidator_Validate")]
        public static async Task<bool> Validate([ActivityTrigger] string order, ILogger log)
        {
            log.LogInformation($"Validating credit card for order #{order}.");

            await Task.Delay(10000);

            return true;
        }
    }


    public static class OrderPaymentProcessor
    {
        [FunctionName("OrderPaymentProcessor_Process")]
        public static async Task<bool> Process([ActivityTrigger] string order, ILogger log)
        {
            log.LogInformation($"Processing payment for order #{order}.");

            await Task.Delay(10000);

            return true;
        }
    }


    public static class OrderDispatcher
    {

        [FunctionName("OrderDispatcher_Dispatch")]
        public static async Task<bool> Dispatch([ActivityTrigger] string order, ILogger log)
        {
            log.LogInformation($"Dispatching items for order #{order}.");

            await Task.Delay(10000);

            return true;
        }
    }


    public static class PaymentNotifier
    {
        [FunctionName("PaymentNotifier_Notify")]
        public static async Task<bool> Notify([ActivityTrigger] string order, ILogger log)
        {
            log.LogInformation($"Notifying payment received to customer for order #{order}.");

            await Task.Delay(10000);

            return true;
        }
    }


}