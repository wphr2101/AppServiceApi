using System.Text;
using System.Text.RegularExpressions;

namespace CustomerOperations.Api.Help;

public sealed record HelpLink(string Label, string Path);
public sealed record HelpAnswer(string Topic, string Title, string Text, string[] Steps, HelpLink[] Links, string Mode);

public static class HelpGuide
{
    public const string AdminMessage = "This must get approval from Mr. Richard Wu";
    public static readonly string[] Topics = ["register", "login", "cart", "customer", "checkout", "orders", "admin", "unknown"];

    public static bool IsAdminQuestion(string question)
    {
        var compact = string.Concat(question.Normalize(NormalizationForm.FormKC)
            .Where(c => char.IsLetterOrDigit(c))).ToLowerInvariant();
        return new[] { "admin", "superuser", "rootaccount", "privilege", "permission", "\u7ba1\u7406", "\u5f8c\u53f0", "\u5f8c\u81fa", "\u540e\u53f0", "\u6b0a\u9650", "\u6743\u9650" }
            .Any(compact.Contains);
    }

    public static bool ContainsPrivateDetails(string question) => Regex.IsMatch(question,
        @"[^\s@]+@[^\s@]+\.[^\s@]+|(?:\d[\s()+.-]*){7,}|\b(?:password|secret|api.?key|token)\s*(?:is|:|=)|\bbearer\s+|\bsk-[a-z0-9]+|\beyJ[a-z0-9_-]+\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        || new[] { "\u5bc6\u78bc\u662f", "\u5bc6\u7801\u662f", "\u5bc6\u78bc:", "\u5bc6\u7801:" }.Any(question.Contains);

    public static string Match(string question)
    {
        var q = question.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        bool Has(params string[] words) => words.Any(q.Contains);
        if (Has("register", "registration", "sign up", "signup", "create account", "create an account", "\u8a3b\u518a", "\u6ce8\u518c", "\u5efa\u7acb\u5e33", "\u5efa\u7acb\u5e10")) return "register";
        if (Has("password", "login", "log in", "sign in", "signin", "account", "\u767b\u5165", "\u767b\u5f55", "\u5bc6\u78bc", "\u5bc6\u7801")) return "login";
        if (Has("checkout", "check out", "place order", "place an order", "submit order", "payment", "pay", "\u7d50\u5e33", "\u7ed3\u8d26", "\u4e0b\u55ae", "\u4e0b\u5355", "\u9001\u51fa\u8a02\u55ae")) return "checkout";
        if (Has("delivery", "address", "recipient", "customer", "shipping", "\u5730\u5740", "\u6536\u4ef6", "\u5ba2\u6236", "\u914d\u9001")) return "customer";
        if (Has("order", "purchase", "\u8a02\u55ae", "\u8ba2\u5355")) return "orders";
        if (Has("cart", "basket", "quantity", "add product", "\u8cfc\u7269\u8eca", "\u8d2d\u7269\u8f66", "\u6578\u91cf", "\u6570\u91cf")) return "cart";
        return "unknown";
    }

    public static HelpAnswer PrivacyAnswer() => new("privacy", "Keep personal details private",
        "Please remove passwords, contact details, addresses and access tokens, then ask a general how-to question. This question was not sent to AI.", [], [], "policy");

    public static HelpAnswer Answer(string topic, string mode = "guide") => topic switch
    {
        "admin" => new("admin", "Administrator access", AdminMessage, [], [], "policy"),
        "register" => new(topic, "Create an account", "Registration creates a customer account, not an administrator account.",
            ["Open Account and choose Register.", "Enter a username (3-100 letters, numbers, dots, underscores or hyphens), a unique password of 15-128 characters, customer name and email. Phone is optional.", "Choose Create account. After registration, use Sign in with your username and password."],
            [new("Open Account", "/account")], mode),
        "login" => new(topic, "Sign in", "Use your existing customer account. Do not share your password in this assistant.",
            ["Open Account and choose Sign in.", "Enter your username and password, then choose Sign in to account.", "If your session has expired, sign in again. There is no password-reset screen in this application; contact the site owner if you cannot recover access."],
            [new("Open Account", "/account")], mode),
        "cart" => new(topic, "Use the shopping cart", "The cart is a session draft and clears when the page reloads.",
            ["Open Products and choose Add beside a product. Out-of-stock products cannot be added.", "Open Cart to change quantities, remove individual items or clear the cart. Quantities are limited by available stock and the per-item limit of 99.", "Review the item count, weight and subtotal. Taxes and delivery fees are not included."],
            [new("Browse Products", "/products"), new("Open Cart", "/cart")], mode),
        "customer" => new(topic, "Add customer and delivery details", "These details are a session draft; editing them does not update your registered profile.",
            ["Open Customer. Enter customer name, email, recipient name and delivery address. Phone is optional.", "Choose Save draft, then return to Cart to review the delivery details.", "The draft clears on reload. It does not create an account or place an order."],
            [new("Open Customer", "/customer"), new("Open Cart", "/cart")], mode),
        "checkout" => new(topic, "Place an order", "This application creates a Pending order. It does not collect payment or process taxes or delivery fees.",
            ["Sign in through Account, add products and save a Customer delivery draft.", "Open Cart and review products, quantities, prices and delivery details.", "Choose Place order yourself. If prices changed, review the latest prices and choose Use latest prices before submitting again.", "If the outcome is uncertain, use Retry same submission. Check My orders rather than creating a second order."],
            [new("Open Account", "/account"), new("Customer details", "/customer"), new("Open Cart", "/cart")], mode),
        "orders" => new(topic, "Find saved orders", "This assistant cannot read your orders, change an order or provide live delivery tracking.",
            ["Sign in through Account.", "Open My orders to see the orders available to your account.", "Select an order to review its saved details and current status in the application."],
            [new("Open My orders", "/orders")], mode),
        _ => new("unknown", "Logistics usage guide", "I can explain account registration, sign-in, customer drafts, the shopping cart, placing an order and viewing saved orders. I cannot perform these actions or answer unrelated questions.",
            ["Try a question such as: How do I create an account?", "Do not include passwords or personal details."],
            [new("Open Account", "/account"), new("Open Cart", "/cart")], "guide"),
    };
}
