using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;

namespace DurableEntitiesDemo
{
    public class Player: IPlayer
    {
        public Player()
        {
            Inventory = new List<string>();
        }
        public string Name { get; set; }

        public List<string> Inventory { get; set; }

        public void Initialise(string name)
        {
            Name = name;
            Inventory = new List<string>();
        }

        public Task AddItemToInventory(AddItemToInventoryParams parameters)
        {
            Inventory.AddRange(Enumerable.Repeat(parameters.Item, parameters.Quantity));
            return Task.CompletedTask;
        }

        [FunctionName(nameof(Player))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx) => ctx.DispatchAsync<Player>();
    }

    public class AddItemToInventoryParams
    {
        public int Quantity { get; set; }
        public string Item { get; set; }

    }

    public interface IPlayer
    {
        Task AddItemToInventory(AddItemToInventoryParams parameters);
    }

    public class ShopItem: IShopItem
    {
        public string Name { get; set; }
        public int Stock { get; set; }


        public Task UpdateStock(int quantity)
        {
            Stock = quantity;
            return Task.CompletedTask;
        }

        public Task<int> GetStock()
        {
            return Task.FromResult(Stock);
        }

        public Task Initialise(string name)
        {
            Name = name;
            Stock = 0;
            return Task.CompletedTask;
        }


        [FunctionName(nameof(ShopItem))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx) => ctx.DispatchAsync<ShopItem>();

    }

    public interface IShopItem
    {
        Task Initialise(string name);
        Task UpdateStock(int quantity);
        Task<int> GetStock();
    }


    public static class GetPlayerFunction
    {
        [FunctionName(nameof(GetPlayerFunction))]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous,  "get",
                Route = "Game/Player/{name}/")]HttpRequestMessage req,
            [DurableClient] IDurableEntityClient client,
            string name)
        {
            var entityId = new EntityId(nameof(Player), name);

            var state = await client.ReadEntityStateAsync<Player>(entityId);

            if (state.EntityExists)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(state.EntityState))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);

        }
    }

    public static class GetShopItemFunction
    {
        [FunctionName(nameof(GetShopItemFunction))]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous,  "get",
                Route = "Game/Shop/Item/{name}/")]HttpRequestMessage req,
            [DurableClient] IDurableEntityClient client,
            string name)
        {
            var entityId = new EntityId(nameof(ShopItem), name);

            var state = await client.ReadEntityStateAsync<ShopItem>(entityId);

            if (state.EntityExists)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(state.EntityState))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    public static class CreatePlayerFunction
    {
        [FunctionName(nameof(CreatePlayerFunction))]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Anonymous,  "post",
                Route = "Game/Player/{name}/create/")]HttpRequestMessage req,
            [DurableClient] IDurableEntityClient client,
            string name)
        {
            var entityId = new EntityId(nameof(Player), name);

            await client.SignalEntityAsync(entityId, nameof(Player.Initialise), name);
        }
    }


    public static class CreateShopItemFunction
    {
        [FunctionName(nameof(CreateShopItemFunction))]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Anonymous,  "post",
                Route = "Game/Shop/Item/{name}/create/")]HttpRequestMessage req,
            [DurableClient] IDurableEntityClient client,
            string name)
        {
            var entityId = new EntityId(nameof(ShopItem), name);

            await client.SignalEntityAsync<IShopItem>(entityId, item => item.Initialise(name));
        }
    }



    public static class UpdateShopItemStockFunction
    {
        [FunctionName(nameof(UpdateShopItemStockFunction))]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Anonymous,  "post",
                Route = "Game/Shop/Item/{name}/update/{quantity}")]HttpRequestMessage req,
            [DurableClient] IDurableEntityClient client,
            string name,
            int quantity)
        {
            var entityId = new EntityId(nameof(ShopItem), name);

            await client.SignalEntityAsync<IShopItem>(entityId, item => item.UpdateStock(quantity));
        }
    }


    public static class AddItemToPlayerInventoryFunction
    {

        [FunctionName("AddItemToPlayerInventoryFunction_Orchestrator")]
        public static async Task RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var input = context.GetInput<AddItemToPlayerInventoryOrchestratorInput>();
            
            var playerEntityId = new EntityId(nameof(Player), input.Player);
            var itemEntityId = new EntityId(nameof(ShopItem), input.Item);

            using (await context.LockAsync(playerEntityId, itemEntityId))
            {
                var playerProxy = context.CreateEntityProxy<IPlayer>(playerEntityId);
                var itemProxy = context.CreateEntityProxy<IShopItem>(itemEntityId);
                
                var itemStock = await itemProxy.GetStock();

                await playerProxy.AddItemToInventory(new AddItemToInventoryParams
                {
                    Quantity = input.Quantity,
                    Item = input.Item
                });

                await itemProxy.UpdateStock(itemStock - input.Quantity);
            }
        }


        [FunctionName(nameof(AddItemToPlayerInventoryFunction))]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Anonymous,  "post",
                Route = "Game/Player/{player}/Add/{item}/{quantity}")]HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient client,
            string player,
            string item,
            int quantity)
        {
            var input = new AddItemToPlayerInventoryOrchestratorInput
            {
                Player = player,
                Item = item,
                Quantity = quantity
            };

            var orchestratorId = await client.StartNewAsync("AddItemToPlayerInventoryFunction_Orchestrator", input);

            await client.WaitForCompletionOrCreateCheckStatusResponseAsync(req, orchestratorId);
        }


        public class AddItemToPlayerInventoryOrchestratorInput
        {
            public string Player { get; set; }
            public string Item { get; set; }
            public int Quantity { get; set; }
        }
    }

}
