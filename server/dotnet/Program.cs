using System.Text.Json;
using Microsoft.Extensions.Options;
using Stripe;

DotNetEnv.Env.Load();
StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");

StripeConfiguration.AppInfo = new AppInfo
{
    Name = "https://github.com/stripe-samples/link",
    Url = "https://github.com/stripe-samples",
    Version = "0.1.0",
};

StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Environment.GetEnvironmentVariable("STATIC_DIR")
});

builder.Services.Configure<StripeOptions>(options =>
{
    options.PublishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY");
    options.SecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
    options.WebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/config", (IOptions<StripeOptions> stripeOptions) =>
     Results.Ok(new { publishableKey = stripeOptions.Value.PublishableKey })
);

app.MapPost("/create-payment-intent", async (HttpRequest request, IOptions<StripeOptions> stripeOptions) =>
{
    try
    {
        var paymentIntentSvc = new PaymentIntentService();
        var piOptions = new PaymentIntentCreateOptions
        {
            Amount = 1999,
            Currency = "usd",
            PaymentMethodTypes = new List<string> { "link", "card" },
        };

        var paymentIntent = await paymentIntentSvc.CreateAsync(piOptions);
        return Results.Ok(new { clientSecret = paymentIntent.ClientSecret, });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            error = new { message = ex.Message }
        });
    }
});

app.MapGet("/payment/next", async (string payment_intent, HttpResponse response) =>
{
    var paymentIntentSvc = new PaymentIntentService();
    var paymentIntent = await paymentIntentSvc.GetAsync(payment_intent, new()
    {
        Expand = new List<string> { "payment_intent" }
    });

    Results.Redirect($"/success?payment_intent_client_secret={paymentIntent.ClientSecret}");
});

app.MapGet("/success", () => Results.Redirect("success.html"));

app.MapPost("/webhook", async (HttpRequest request, IOptions<StripeOptions> options) =>
{
    var json = await new StreamReader(request.Body).ReadToEndAsync();
    Event stripeEvent;
    try
    {
        stripeEvent = EventUtility.ConstructEvent(
            json,
            request.Headers["Stripe-Signature"],
            options.Value.WebhookSecret
        );

    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Something failed");
        return Results.BadRequest();
    }

    if (stripeEvent.Type == "payment_intent.succeeded")
    {
        var paymentIntent = stripeEvent.Data.Object as Stripe.PaymentIntent;
        app.Logger.LogInformation($"💰 Payment received!");
    }

    return Results.Ok(new { status = "success" });
});

app.Run();
