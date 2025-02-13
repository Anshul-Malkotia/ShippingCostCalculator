using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using static DeliveryCostAPI.Controllers.DeliveryCostService;

namespace DeliveryCostAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeliveryCostController : ControllerBase
    {
        private readonly DeliveryCostService _deliveryCostService;

        public DeliveryCostController()
        {
            _deliveryCostService = new DeliveryCostService();
        }

        [HttpPost]
        [Route("calculate-cost")]
        public IActionResult CalculateDeliveryCost([FromBody] DeliveryRequest request)
        {
            if (request == null || request.ProductQuantities.Count == 0)
            {
                return BadRequest("Invalid request. Please provide product quantities.");
            }

            var result = _deliveryCostService.CalculateMinimumCost(request);
            return Ok(result);
        }
    }

    public class DeliveryRequest
    {
        public Dictionary<string, double> ProductQuantities { get; set; } = new();
    }

    public class DeliveryCostService
    {
        private readonly List<Warehouse> Warehouses;
        private const double BaseDistanceCost = 10;
        private const double ExtraWeightCostPer5Kg = 8;
        private const double BaseWeightLimit = 5;

        private readonly Dictionary<(string, string), double> Distances = new()
        {
            { ("C1", "L1"), 3 }, { ("L1", "C1"), 3 },
    { ("C2", "L1"), 2.5 }, { ("L1", "C2"), 2.5 },
    { ("C3", "L1"), 2 }, { ("L1", "C3"), 2 },
    { ("C1", "C2"), 4 }, { ("C2", "C1"), 4 },
    { ("C1", "C3"), 5 }, { ("C3", "C1"), 5 },
    { ("C2", "C3"), 3 }, { ("C3", "C2"), 3 }
        };

        private readonly Dictionary<string, double> ProductWeights = new()
        {
            { "A", 3 }, { "B", 2 }, { "C", 8 },
            { "D", 12 }, { "E", 25 }, { "F", 15 },
            { "G", 0.5 }, { "H", 1 }, { "I", 2 }
        };

        public DeliveryCostService()
        {
            Warehouses = new List<Warehouse>
            {
                new Warehouse("C1", new Dictionary<string, double> { { "A", 3 }, { "B", 2 }, { "C", 8 } }),
                new Warehouse("C2", new Dictionary<string, double> { { "D", 12 }, { "E", 25 }, { "F", 15 } }),
                new Warehouse("C3", new Dictionary<string, double> { { "G", 0.5 }, { "H", 1 }, { "I", 2 } })
            };
        }

        public object CalculateMinimumCost(DeliveryRequest request)
        {
            var combinations = GenerateCombinations(request.ProductQuantities);  // All possible warehouse combinations
            double minimumCost = double.MaxValue;
            List<string> optimalCombination = null;
            List<string> optimalDetails = null;

            foreach (var combination in combinations)
            {
                double currentCost = EvaluateCombinationCost(combination, out var deliveryDetails);

                if (currentCost < minimumCost)
                {
                    minimumCost = currentCost;
                    optimalCombination = combination.Select(c => c.Warehouse).Distinct().ToList();
                    optimalDetails = new List<string>(deliveryDetails);
                }
            }

            return new
            {
                MinimumCost = minimumCost,
                OptimalCombination = optimalCombination,
                DeliveryDetails = optimalDetails
            };
        }

        private double EvaluateCombinationCost(List<CombinationStep> combination, out List<string> deliveryDetails)
        {
            deliveryDetails = new List<string>();
            double singleTripCost = CalculateSingleTripCost(combination, out var singleTripDetails);
            double multiTripCost = CalculateMultiTripCost(combination, out var multiTripDetails);

            // Choose the cheaper option between single-trip and multi-trip
            if (singleTripCost <= multiTripCost)
            {
                deliveryDetails.AddRange(singleTripDetails);
                return singleTripCost;
            }
            else
            {
                deliveryDetails.AddRange(multiTripDetails);
                return multiTripCost;
            }
        }

        private double CalculateSingleTripCost(List<CombinationStep> combination, out List<string> deliveryDetails)
        {
            deliveryDetails = new List<string>();
            double totalWeight = combination.Sum(step => step.Weight);
            string startWarehouse = combination.First().Warehouse;
            string endWarehouse = combination.Last().Warehouse;

            deliveryDetails.Add($"Starting from {startWarehouse} with total weight: {totalWeight} kg");

            double distanceToL1 = 0;

            if (startWarehouse== endWarehouse)
            {
                endWarehouse = "L1";
                distanceToL1 = Distances[(startWarehouse, endWarehouse)];
            }
            else
            {
                distanceToL1 = Distances[(startWarehouse, endWarehouse)];
                distanceToL1 += Distances[(endWarehouse, "L1")];
            }

            //double distanceToL1 = Distances[(startWarehouse, endWarehouse)];
            double cost = CalculateCost(totalWeight, distanceToL1);

            deliveryDetails.Add($"Delivered everything to L1 with {totalWeight} kg at cost {cost}");

            return cost;
        }

        private double CalculateMultiTripCost(List<CombinationStep> combination, out List<string> deliveryDetails)
        {
            deliveryDetails = new List<string>();
            double totalCost = 0;
            double currentWeight = 0;
            string currentLocation = combination.First().Warehouse;
            var temp = combination.GroupBy(e=>e.Warehouse).ToList();

            foreach (var step in temp)
            {
                var prod = step.GroupBy(e => e.Products).ToDictionary(e=>e).ToList();
                var weight = step.Sum(e => e.Weight);
                if (currentLocation != step.Key)
                {
                    double distanceToWarehouse = Distances[(currentLocation, step.Key)];
                    totalCost += CalculateCost(currentWeight, distanceToWarehouse);
                    deliveryDetails.Add($"Travelled from {currentLocation} to {step.Key} with {currentWeight} kg at cost {CalculateCost(currentWeight, distanceToWarehouse)}");
                }

                currentWeight += weight;
                deliveryDetails.Add($"Picked up {string.Join(", ", prod.Select(p => $"{p.Key.Key[0].Name} of {p.Key.Key[0].Quantity}"))} from {step.Key}");

                double distanceToL1 = Distances[(step.Key, "L1")];
                totalCost += CalculateCost(currentWeight, distanceToL1);
                deliveryDetails.Add($"Delivered to L1 with {currentWeight} kg at cost {CalculateCost(currentWeight, distanceToL1)}");

                currentLocation = "L1";  // Return to L1 after each delivery
                currentWeight = 0;
            }

            return totalCost;
        }

        private double CalculateCost(double weight, double distance)
        {
            if (weight <= 5) return distance * 10;  // Base cost for 0-5 kg
            double extraWeightBlocks = (weight - 5) / 5;
            return distance * (10 + Math.Ceiling(extraWeightBlocks) * 8);  // Cost increases for every extra 5 kg block
        }

        private List<List<CombinationStep>> GenerateCombinations(Dictionary<string, double> productQuantities)
        {
            var combinations = new List<List<CombinationStep>>();

            // Generate all possible combinations of warehouses to fulfill the product quantities
            var warehouseCombinations = GetWarehouseCombinations(productQuantities);
            foreach (var warehouseCombination in warehouseCombinations)
            {
                var steps = new List<CombinationStep>();

                foreach (var warehouse in warehouseCombination)
                {
                    var products = warehouse.Products.Select(p => new Product { Name = p.Key, Quantity = p.Value }).ToList();
                    double totalWeight = products.Sum(p => p.Quantity * ProductWeights[p.Name]);
                    steps.Add(new CombinationStep { Warehouse = warehouse.Name, Products = products, Weight = totalWeight });
                }

                combinations.Add(steps);
            }

            return combinations;
        }

        private List<List<WarehouseCombination>> GetWarehouseCombinations(Dictionary<string, double> productQuantities)
        {
            var combinations = new List<List<WarehouseCombination>>();

            // Find all warehouses that stock each product
            var productSources = productQuantities.Keys.ToDictionary(
                product => product,
                product => Warehouses.Where(w => w.Stock.ContainsKey(product)).ToList()
            );

            // Generate all possible combinations of warehouses that can fulfill the order
            GenerateCombinationsRecursive(productQuantities, productSources, new List<WarehouseCombination>(), combinations);

            return combinations;
        }

        private void GenerateCombinationsRecursive(
            Dictionary<string, double> remainingProducts,
            Dictionary<string, List<Warehouse>> productSources,
            List<WarehouseCombination> currentCombination,
            List<List<WarehouseCombination>> allCombinations)
        {
            if (remainingProducts.Count == 0)
            {
                allCombinations.Add(new List<WarehouseCombination>(currentCombination));
                return;
            }

            var product = remainingProducts.Keys.First();
            var quantity = remainingProducts[product];
            var possibleWarehouses = productSources[product];

            foreach (var warehouse in possibleWarehouses)
            {
                double availableQuantity = warehouse.Stock[product];
                double pickedQuantity = Math.Min(quantity, availableQuantity);

                var updatedCombination = new WarehouseCombination
                {
                    Name = warehouse.Name,
                    Products = new Dictionary<string, double> { { product, pickedQuantity } }
                };

                var updatedProducts = new Dictionary<string, double>(remainingProducts);
                updatedProducts[product] -= pickedQuantity;

                if (updatedProducts[product] <= 0)
                {
                    updatedProducts.Remove(product);
                }

                currentCombination.Add(updatedCombination);
                GenerateCombinationsRecursive(updatedProducts, productSources, currentCombination, allCombinations);
                currentCombination.RemoveAt(currentCombination.Count - 1);
            }
        }

        private Warehouse FindNearestWarehouseWithProducts(Dictionary<string, double> remainingProducts, string currentLocation, out Dictionary<string, double> productsPicked)
        {
            productsPicked = new Dictionary<string, double>();
            Warehouse nearestWarehouse = null;
            double shortestDistance = double.MaxValue;

            foreach (var warehouse in Warehouses)
            {
                var availableProducts = warehouse.Stock.Keys.Intersect(remainingProducts.Keys).ToList();
                if (availableProducts.Count > 0)
                {
                    if (currentLocation != warehouse.Name)
                    {
                        double distance = Distances[(currentLocation, warehouse.Name)];
                        if (distance < shortestDistance)
                        {
                            shortestDistance = distance;
                            nearestWarehouse = warehouse;

                            // Pick all products available at this warehouse
                            productsPicked = availableProducts.ToDictionary(p => p, p => remainingProducts[p]);
                        }
                    }
                }
            }

            return nearestWarehouse;
        }

    }

    public class Warehouse
    {
        public string Name { get; set; }
        public Dictionary<string, double> Stock { get; set; }

        public Warehouse(string name, Dictionary<string, double> stock)
        {
            Name = name;
            Stock = stock;
        }
    }
    public class CombinationStep
    {
        public string Warehouse { get; set; }
        public List<Product> Products { get; set; }
        public double Weight { get; set; }
    }

    public class Product
    {
        public string Name { get; set; }
        public double Quantity { get; set; }
    }
    
    public class WarehouseCombination
    {
        public string Name { get; set; }
        public Dictionary<string, double> Products { get; set; }
    }

}
