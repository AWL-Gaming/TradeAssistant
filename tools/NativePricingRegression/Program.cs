using System.Reflection;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Components.Store.Internal;
using Eco.Gameplay.Items;
using Eco.Core.Controller;
using Eco.Shared.Utils;
using Eco.Shared.View;
using Eco.Gameplay.Bonuses;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Players;
using Eco.Gameplay.Skills;
using TradeAssistant;

class Program
{
    static int Main()
    {
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            var dependencyRoot = Environment.GetEnvironmentVariable("ECO_NATIVE_DEPENDENCY_DIR");
            if (string.IsNullOrWhiteSpace(dependencyRoot)) dependencyRoot = Environment.GetEnvironmentVariable("ECO_NATIVE_DIR");
            if (string.IsNullOrWhiteSpace(dependencyRoot)) dependencyRoot = AppContext.BaseDirectory;
            var path = Path.Combine(dependencyRoot, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        try { return Run(); }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
    static int Run()
    {
        var first = (FixtureItem)RuntimeHelpers.GetUninitializedObject(typeof(FirstFixtureItem));
        first.FixtureID = 101;
        var second = (FixtureItem)RuntimeHelpers.GetUninitializedObject(typeof(SecondFixtureItem));
        second.FixtureID = 102;
        var manager = (ControllerManager)RuntimeHelpers.GetUninitializedObject(typeof(ControllerManager));
        typeof(ControllerManager).GetField("idToClassInfos", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(manager, new ConcurrentDictionary<int, ViewClassInfo>(new[] {
                new KeyValuePair<int,ViewClassInfo>(101, new ViewClassInfo {Type = first.GetType(), TypeID = 101}),
                new KeyValuePair<int,ViewClassInfo>(102, new ViewClassInfo {Type = second.GetType(), TypeID = 102})
            }));
        typeof(Singleton<ControllerManager>).GetProperty(nameof(Singleton<ControllerManager>.Obj))!.SetValue(null, manager);
        typeof(Item).GetField("itemTypeDictionary", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new Dictionary<Type,Item> {{first.GetType(),first},{second.GetType(),second}});
        typeof(Item).GetProperty(nameof(Item.AllItemsExceptHidden))!.SetValue(null, new Item[] {first, second});
        Check(ReferenceEquals(Item.Get(101), first) && ReferenceEquals(Item.Get(102), second), "native item registry resolves both fixture IDs");
        var offer = new TradeOffer(new FixtureTag(), false) {Price = 42};
        Check(offer.OfferedItemTypeIDs().SequenceEqual(new[] {101,102}), "real native tagged offer expands to two item IDs");
        var concrete = new TradeOffer(first, 42, false);
        Check(concrete.OfferedItemTypeIDs().SequenceEqual(new[] {101}), "concrete offer stays one item");
        Check(concrete.MatchesTypeID(101) && !concrete.MatchesTypeID(102), "concrete offer matching is specific");
        var data = new StoreItemData();
        var category = new StoreCategory();
        category.Offers.Add(offer);
        data.SellCategories.Add(category);
        var store = (StoreComponent)RuntimeHelpers.GetUninitializedObject(typeof(StoreComponent));
        SetBacking(store, nameof(StoreComponent.StoreData), data);
        var calc = (TradeAssistantCalculator)RuntimeHelpers.GetUninitializedObject(typeof(TradeAssistantCalculator));
        var config = new TradeAssistant.UserConfig();
        config.FrozenSellPrices.Add(102);
        SetBacking(calc, nameof(TradeAssistantCalculator.Config), config);
        SetBacking(calc, nameof(TradeAssistantCalculator.Store), store);
        var cache = new Dictionary<int,float>{{101,42},{102,42}};
        SetBacking(calc, "StoreSellPrices", cache);
        calc.SetSellPrice(first, 43);
        Console.WriteLine($"Frozen offer: {offer.Price}; cached items: {cache[101]}, {cache[102]}");
        Check(offer.Price == 42 && cache[101] == 42 && cache[102] == 42, "freezing one tagged item preserves its entire shared offer and cache");
        category.Offers.Clear();
        category.Offers.Add(concrete);
        config.FrozenSellPrices.Clear();
        config.FrozenSellPrices.Add(101);
        calc.SetSellPrice(first, 43);
        Check(concrete.Price == 42 && cache[101] == 42, "a frozen concrete offer remains unchanged");
        config.FrozenSellPrices.Clear();
        cache[101] = 19;
        calc.SetSellPrice(first, 42);
        Check(concrete.Price == 42 && cache.Count == 1 && cache[101] == 42, "ordinary concrete offer keeps its price and rebuilds the actual cache");
        calc.SetSellPrice(second, 71);
        Check(cache.Count == 1 && cache[101] == 42 && !cache.ContainsKey(102), "an item with no sell offer creates no synthetic cached price");
        category.Offers.Clear();
        category.Offers.Add(offer);
        cache[101] = 11;
        cache[102] = 12;
        calc.SetSellPrice(first, 42);
        Check(offer.Price == 42 && cache[101] == 42 && cache[102] == 42, "unfrozen tagged offer refreshes every represented cached item");
        category.Offers.Add(concrete);
        config.FrozenSellPrices.Add(102);
        cache[101] = 10;
        calc.SetSellPrice(first, 42);
        Check(offer.Price == 42 && concrete.Price == 42 && cache[101] == 42 && cache[102] == 42, "overlapping concrete offer retains a frozen shared offer's actual minimum price");
        var buyCache = new Dictionary<int,float>();
        SetBacking(calc, "StoreBuyPrices", buyCache);
        calc.SetBuyPrice(second, 70);
        Check(buyCache.Count == 0, "an item with no buy offer creates no synthetic cached price");
        var buyCategory = new StoreCategory();
        buyCategory.Offers.Add(new TradeOffer(new FixtureTag(), true) {Price = 42});
        data.BuyCategories.Add(buyCategory);
        calc.SetBuyPrice(first, 42);
        Check(buyCache.Count == 2 && buyCache[101] == 42 && buyCache[102] == 42, "tagged buy offer refreshes every represented cached item");
        buyCategory.Offers.Add(new TradeOffer(first, 43, true));
        calc.SetBuyPrice(second, 42);
        Check(buyCache[101] == 43 && buyCache[102] == 42, "overlapping buy offers retain the actual maximum price");
        config.FrozenSellPrices.Clear();
        category.Offers.Clear();
        category.Offers.Add(concrete);
        var sellUpdates = calc.SetSellPrice(first, 44);
        Check(concrete.Price == 44 && cache.Count == 1 && cache[101] == 44 && sellUpdates.Count == 1, "concrete sell price-changing notification path updates the real offer and cache");
        var buyUpdates = calc.SetBuyPrice(second, 45);
        Check(buyCategory.Offers[0].Price == 45 && buyCache[101] == 45 && buyCache[102] == 45 && buyUpdates.Count == 1, "shared buy price-changing notification path updates both real cached prices");
        CheckSharedOrderPrices(calc, first, second, category, buyCategory, cache, buyCache, config);
        CheckCraftingQuantities(first, second);
        Console.WriteLine("PASS: native TradeAssistant pricing regressions");
        return 0;
    }
    static void SetBacking(object value, string name, object fieldValue) =>
        value.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(value, fieldValue);
    static void CheckSharedOrderPrices(TradeAssistantCalculator calc, Item first, Item second, StoreCategory sells, StoreCategory buys,
        Dictionary<int, float> sellCache, Dictionary<int, float> buyCache, TradeAssistant.UserConfig config)
    {
        var skipped = new List<Eco.Shared.Localization.LocString>();
        var sellOffer = new TradeOffer(new FixtureTag(), false) {Price = 42};
        sells.Offers.Clear();
        sells.Offers.Add(sellOffer);
        config.FrozenSellPrices.Clear();
        var changes = calc.SetSellPrices(new Dictionary<int, float> {{101, 55}, {102, 90}}, skipped);
        Check(sellOffer.Price == 90 && sellCache[101] == 90 && sellCache[102] == 90 && changes.Count == 1 && skipped.Count == 0,
            "shared sell order covers its highest calculated item cost");
        sellOffer.Price = 42;
        calc.SetSellPrices(new Dictionary<int, float> {{102, 90}, {101, 55}}, skipped);
        Check(sellOffer.Price == 90, "reversing calculated item order cannot change a shared sell price");
        var concrete = new TradeOffer(first, 42, false);
        sells.Offers.Add(concrete);
        calc.SetSellPrices(new Dictionary<int, float> {{101, 55}, {102, 90}}, skipped);
        Check(concrete.Price == 55 && sellOffer.Price == 90 && sellCache[101] == 55 && sellCache[102] == 90,
            "concrete prices stay independent and the shared cache uses actual minimum sell offers");
        config.FrozenSellPrices.Add(102);
        calc.SetSellPrices(new Dictionary<int, float> {{101, 63}, {102, 99}}, skipped);
        Check(sellOffer.Price == 90 && concrete.Price == 63 && sellCache[101] == 63 && sellCache[102] == 90,
            "batch pricing preserves a frozen shared offer while updating an eligible concrete offer");
        config.FrozenSellPrices.Clear();
        calc.SetSellPrices(new Dictionary<int, float> {{101, 64}}, skipped);
        Check(sellOffer.Price == 90 && concrete.Price == 64 && skipped.Count == 1,
            "incomplete shared sell costs preserve the shared price and explain the skipped order");
        skipped.Clear();
        calc.SetSellPrices(new Dictionary<int, float> {{101, 64}, {102, float.NaN}}, skipped);
        Check(sellOffer.Price == 90 && skipped.Count == 1, "a non-finite shared item cost never changes the sell order");
        skipped.Clear();
        calc.SetSellPrices(new Dictionary<int, float> {{101, 64}, {102, -1}}, skipped);
        Check(sellOffer.Price == 90 && skipped.Count == 1, "a negative shared item cost never changes the sell order");
        skipped.Clear();
        var buyOffer = new TradeOffer(new FixtureTag(), true) {Price = 90};
        buys.Offers.Clear();
        buys.Offers.Add(buyOffer);
        changes = calc.SetBuyPrices(new Dictionary<int, float> {{101, 75}, {102, 33}}, skipped);
        Check(buyOffer.Price == 33 && buyCache[101] == 33 && buyCache[102] == 33 && changes.Count == 1,
            "shared buy order uses its lowest calculated item cost");
        buyOffer.Price = 90;
        calc.SetBuyPrices(new Dictionary<int, float> {{102, 33}, {101, 75}}, skipped);
        Check(buyOffer.Price == 33, "reversing calculated item order cannot change a shared buy price");
        var concreteBuy = new TradeOffer(first, 80, true);
        buys.Offers.Add(concreteBuy);
        calc.SetBuyPrices(new Dictionary<int, float> {{101, 75}}, skipped);
        Check(buyOffer.Price == 33 && concreteBuy.Price == 75 && buyCache[101] == 75 && buyCache[102] == 33 && skipped.Count == 1,
            "incomplete shared buy costs retain the shared order and actual maximum cached buy price");
    }
    static void CheckCraftingQuantities(Item first, Item second)
    {
        var user = (User)RuntimeHelpers.GetUninitializedObject(typeof(User));
        var talents = new Talentset();
        talents.Talents.Add(typeof(FixtureTalent));
        SetBacking(user, nameof(User.Talentset), talents);
        var talent = new FixtureTalent();
        foreach (var (action, multiplier) in new[] {
            (BonusAction.ResourceCost, 0.5f), (BonusAction.LaborCost, 0.75f), (BonusAction.Yield, 1.5f) })
            talent.Bonuses.Add(new Bonus {
                Causes = new() { new ActionCause {Action = action} },
                Effects = new() { new BonusEffectMultiplicative {Value = multiplier} }
            });
        TalentManager.TypeToTalent = new Dictionary<Type, Talent> {{typeof(FixtureTalent), talent}};
        var resource = new BonusContext {Source = user, Action = BonusAction.ResourceCost};
        var yield = new BonusContext {Source = user, Action = BonusAction.Yield};
        var labor = new BonusContext {Source = user, Action = BonusAction.LaborCost};
        Check(BonusManager.ApplyBonuses(labor, 8) == 6, "native crafting preview applies the labor bonus independently");
        var ingredient = new IngredientElement(first, 10, staticIngredient: true);
        var ingredientMethod = typeof(TradeAssistantCalculator).GetMethod("GetAdjustedIngredientQuantity", BindingFlags.Static | BindingFlags.NonPublic)!;
        float IngredientCount() => (float)ingredientMethod.Invoke(null, new object?[] {ingredient, null, null, resource})!;
        Check(IngredientCount() == 10, "fixed ingredients keep their quantity despite resource bonuses");
        SetBacking(ingredient, nameof(IngredientElement.Quantity), new MultiDynamicValue(MultiDynamicOps.Sum, new ConstantValue(10)));
        Check(IngredientCount() == 5, "dynamic ingredients apply the native resource bonus once");
        var recipe = (Recipe)RuntimeHelpers.GetUninitializedObject(typeof(Recipe));
        typeof(Recipe).GetProperty(nameof(Recipe.Ingredients))!.SetValue(recipe, new List<IngredientElement> {ingredient});
        var productMethod = typeof(TradeAssistantCalculator).GetMethod("GetAdjustedProductQuantity", BindingFlags.Static | BindingFlags.NonPublic)!;
        float ProductCount(Item item, float quantity, bool? refund = null) =>
            (float)productMethod.Invoke(null, new object?[] {new FixtureProduct(item, quantity, refund), recipe, null, null, resource, yield})!;
        Check(ProductCount(second, 4) == 6, "ordinary products apply the native yield bonus once");
        Check(ProductCount(first, 2) == 1, "discounted ingredient refunds use resource discounts and skip yield bonuses");
        SetBacking(ingredient, nameof(IngredientElement.Quantity), new ConstantValue(10));
        Check(ProductCount(first, 2) == 2, "fixed ingredient refunds keep their original quantity");
        Check(ProductCount(first, 2, false) == 3, "an explicit non-refund product receives yield even if its item is an ingredient");
        Check(ProductCount(second, 2, true) == 2, "an explicit refund without a discounted ingredient skips yield");
    }
    static void Check(bool pass, string name)
    {
        Console.WriteLine((pass ? "PASS: " : "FAIL: ") + name);
        if (!pass) throw new Exception(name);
    }
}
abstract class FixtureItem : Item
{
    public int FixtureID;
    public override int TypeID => FixtureID;
}
sealed class FirstFixtureItem : FixtureItem { }
sealed class SecondFixtureItem : FixtureItem { }
sealed class FixtureTalent : Talent { }
sealed class FixtureProduct : CraftingElement
{
    public FixtureProduct(Item item, float quantity, bool? refund)
    {
        Item = item;
        Quantity = new ConstantValue(quantity);
        RefundsIngredient = refund;
    }
}
sealed class FixtureTag : Tag
{
    public FixtureTag() : base("NativeRegressionOnly") { }
    public override bool RepresentsItem(Item item) => item is FixtureItem;
}
