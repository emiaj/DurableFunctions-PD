using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json.Linq;

namespace DurableEntitiesDemo
{
    public static class CounterFunction
    {
        [FunctionName("Counter")]
        public static void Counter([EntityTrigger] IDurableEntityContext ctx)
        {
            switch (ctx.OperationName.ToLowerInvariant())
            {
                case "add":
                    ctx.SetState(ctx.GetState<int>() + ctx.GetInput<int>());
                    break;
                case "reset":
                    ctx.SetState(0);
                    break;
                case "get":
                    ctx.Return(ctx.GetState<int>());
                    break;
            }
        }
    }


    public static class IncrementCounter
    {
        [FunctionName("IncrementCounter")]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Anonymous,  "post",
                Route = "Counter/{name}/increment/{value}")]HttpRequestMessage req,
            [DurableClient] IDurableEntityClient client,
            string name,
            int value)
        {

            var entityId = new EntityId("Counter", name);

            await client.SignalEntityAsync(entityId, "add", value);
        }
    }


    public static class ResetCounter
    {
        [FunctionName("ResetCounter")]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Anonymous,  "post",
                Route = "Counter/{name}/reset")]HttpRequestMessage req,
            [DurableClient] IDurableEntityClient client,
            string name)
        {

            var entityId = new EntityId("Counter", name);

            await client.SignalEntityAsync(entityId, "reset");
        }
    }

    public static class GetCounter
    {

        [FunctionName("GetCounter_Orchestrator")]
        public static async Task<int> RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var entityId = context.GetInput<EntityId>();
            return await context.CallEntityAsync<int>(entityId, "get", null);
        }

        [FunctionName("GetCounter")]
        public static async Task<JToken> Run([HttpTrigger(AuthorizationLevel.Anonymous,  "get",
                Route = "Counter/{name}")]HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient client,
            string name)
        {

            var entityId = new EntityId("Counter", name);
            var orchestratorId = await client.StartNewAsync("GetCounter_Orchestrator", entityId);
            
            await client.WaitForCompletionOrCreateCheckStatusResponseAsync(req, orchestratorId);

            var status = await client.GetStatusAsync(orchestratorId);

            return status.Output;
        }


        public static class GetCounterState
        {
            [FunctionName("GetCounterState")]
            public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous,  "get",
                    Route = "Counter/{name}/state")]HttpRequestMessage req,
                [DurableClient] IDurableEntityClient client,
                string name)
            {

                var entityId = new EntityId("Counter", name);

                var state = await client.ReadEntityStateAsync<int>(entityId);

                if (state.EntityExists)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(state.EntityState.ToString())
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

    }



}