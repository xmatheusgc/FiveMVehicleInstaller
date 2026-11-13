using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;

namespace FiveMVehicleInstaller.Tests;

public sealed class ResourceNameTests
{
    [Theory]
    [InlineData("Nissan Silvia Ks 1998 S14 Kouki", "nissan_silvia_ks_1998_s14_kouki")]
    [InlineData("Toyota Chaser JZX100", "toyota_chaser_jzx100")]
    [InlineData("Nissan Silvia S13", "nissan_silvia_s13")]
    [InlineData("Nissan 180SX Type X", "nissan_180sx_type_x")]
    [InlineData("Nissan-Silvia-S15-Spec-R", "nissan-silvia-s15-spec-r")]
    [InlineData("  My  Garage \t Pack  ", "my_garage_pack")]
    [InlineData("car_01-test", "car_01-test")]
    public void NormalizesFolderNamesWithoutRelaxingBackendValidation(string input, string expected)
    {
        var actual = ResourceNames.NormalizeInput(input);
        Assert.Equal(expected, actual);
        Assert.Equal("", ResourceNames.ValidationError(actual));
        Assert.Equal(expected, SafePaths.ValidateResourceName(actual));
    }

    [Theory]
    [InlineData("")][InlineData(" ")][InlineData("CON")][InlineData("nul")]
    [InlineData("../car")][InlineData("C:\\cars")][InlineData("car/other")]
    [InlineData("_car")][InlineData("car.")][InlineData("car\n")]
    public void BackendRejectsInvalidNamesIncludingTrailingNewlines(string name)
    {
        Assert.NotEmpty(ResourceNames.ValidationError(name));
        Assert.Throws<InvalidDataException>(() => SafePaths.ValidateResourceName(name));
    }

    [Fact]
    public void LimitsNamesToSixtyFourCharactersAndKeepsReservedNamesBlockedAfterNormalization()
    {
        Assert.Equal("", ResourceNames.ValidationError(new string('a', 64)));
        Assert.NotEmpty(ResourceNames.ValidationError(new string('a', 65)));
        Assert.NotEmpty(ResourceNames.ValidationError(ResourceNames.NormalizeInput(" CON ")));
        Assert.NotEmpty(ResourceNames.ValidationError(ResourceNames.NormalizeInput("../Car")));
    }
}