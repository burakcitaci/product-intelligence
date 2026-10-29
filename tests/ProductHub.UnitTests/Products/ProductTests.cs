using ProductHub.Domain.Products;

namespace ProductHub.UnitTests.Products;

public sealed class ProductTests
{
    [Fact]
    public void Create_NormalizesSkuAndPreservesFacts()
    {
        var now = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
        var product = Product.Create(
            " abc-123 ",
            "Widget",
            "Acme",
            attributes: [new ProductAttribute("Weight", "2", "kg")],
            now: now
        );
        Assert.Equal("ABC-123", product.Sku);
        Assert.Equal(now, product.CreatedAt);
        Assert.Single(product.Attributes);
    }

    [Fact]
    public void Create_RejectsDuplicateAttributeNames()
    {
        var attributes = new[]
        {
            new ProductAttribute("Color", "Black"),
            new ProductAttribute("color", "Blue"),
        };
        Assert.Throws<ArgumentException>(() =>
            Product.Create("sku", "Name", "Brand", attributes: attributes)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_RejectsBlankSku(string sku) =>
        Assert.Throws<ArgumentException>(() => Product.Create(sku, "Name", "Brand"));
}
