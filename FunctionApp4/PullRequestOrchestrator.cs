using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace FunctionApp4
{
    public static class PullRequestOrchestrator
    {
        [FunctionName("PullRequestOrchestrator")]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            context.SetCustomStatus(PullRequestStatus.Pending.ToString());


            var approved = context.WaitForExternalEvent(ApprovePullRequest.PullRequestApprovedEvent);
            var rejected = context.WaitForExternalEvent(RejectPullRequest.PullRequestRejectedEvent);
            var expired = context.CreateTimer(context.CurrentUtcDateTime.Add(TimeSpan.FromSeconds(20)), CancellationToken.None);

            var result = await Task.WhenAny(approved, rejected, expired);

            if (result == expired)
            {
                context.SetCustomStatus(PullRequestStatus.Expired.ToString());
            }
            else if (result == approved)
            {
                context.SetCustomStatus(PullRequestStatus.Approved.ToString());
            }
            else
            {
                context.SetCustomStatus(PullRequestStatus.Rejected.ToString());
            }

        }


        [FunctionName("PullRequestOrchestrator_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous,  "post",
                Route = "PullRequestOrchestrator_HttpStart/{id}")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            int id,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = PullRequestOrchestratorHelper.GetOrchestratorInstanceId(id);

            await starter.StartNewAsync("PullRequestOrchestrator", instanceId, null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }


    public static class PullRequestOrchestratorHelper
    {
        public static string GetOrchestratorInstanceId(int pullRequestId)
        {
            return $"PullRequestOrchestrator_{pullRequestId}";
        }
    }


    public enum PullRequestStatus
    {
        Pending,
        Approved,
        Rejected,
        Expired
    }



    public static class ApprovePullRequest
    {
        public const string PullRequestApprovedEvent = "PullRequestApproved";

        [FunctionName("ApprovePullRequest")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ApprovePullRequest/{id}"
            )]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            int id,
            ILogger log)
        {

            var instanceId = PullRequestOrchestratorHelper.GetOrchestratorInstanceId(id);

            await starter.RaiseEventAsync(instanceId, PullRequestApprovedEvent);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }


    public static class RejectPullRequest
    {
        public const string PullRequestRejectedEvent = "PullRequestRejected";


        [FunctionName("RejectPullRequest")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "RejectPullRequest/{id}"
            )]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            int id,
            ILogger log)
        {

            var instanceId = PullRequestOrchestratorHelper.GetOrchestratorInstanceId(id);

            await starter.RaiseEventAsync(instanceId, PullRequestRejectedEvent);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }


}