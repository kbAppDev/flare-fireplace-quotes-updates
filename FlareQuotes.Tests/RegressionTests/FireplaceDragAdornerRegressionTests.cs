using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FlareQuotes.App.Views;
using Xunit;

namespace FlareQuotes.Tests.RegressionTests;

public sealed class FireplaceDragAdornerRegressionTests
{
    [Fact]
    public void DragAdornerInitializesItsVisualCollectionBeforeWpfPropertyPropagation()
    {
        Exception? capturedException = null;
        object? adorner = null;
        var visualChildCount = -1;

        var thread = new Thread(
            () =>
            {
                try
                {
                    var adornerType = typeof(MainWindow).GetNestedType(
                        "FireplaceCardDragAdorner",
                        BindingFlags.NonPublic);

                    if (adornerType is null)
                        throw new InvalidOperationException("The fireplace drag adorner type was not found.");

                    var constructor = adornerType.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        new[] { typeof(UIElement), typeof(ImageSource), typeof(double), typeof(double) },
                        modifiers: null);

                    if (constructor is null)
                        throw new InvalidOperationException("The fireplace drag adorner constructor was not found.");

                    adorner = constructor.Invoke(new object[] { new Border(), new DrawingImage(), 240d, 120d });
                    visualChildCount = VisualTreeHelper.GetChildrenCount((DependencyObject)adorner);
                }
                catch (TargetInvocationException exception)
                {
                    capturedException = exception.InnerException ?? exception;
                }
                catch (Exception exception)
                {
                    capturedException = exception;
                }
            });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The STA adorner test did not finish.");
        Assert.Null(capturedException);
        Assert.NotNull(adorner);
        Assert.Equal(1, visualChildCount);
    }
}
