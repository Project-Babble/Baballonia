# AvaloniaSearchableComboBox

A searchable ComboBox for [Avalonia UI](https://avaloniaui.net/) that allows users to filter items by typing. This is especially useful when working with large lists, like selecting a supplier, product, or country from hundreds of options.

Avalonia does not include this component by default, but this project provides a custom implementation that can be integrated easily into your own applications.

---

## ✅ Features

- Auto-filter items based on user input
- Built as a **TemplatedControl** for styling and customization
- Simple integration with MVVM
- Compatible with `.NET 8.0`
- Supports both complex and simple data types

---

## 📦 How to Use

### 1. Add the Project

Add the `AvaloniaSearchableComboBox` class library to your solution.

Install the `Avalonia` NuGet package in this project.

### 2. Reference in App

In your main project, reference the library and include the style resource in `App.axaml`:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://AvaloniaSearchableComboBox/Themes/SearchableComboBox.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

---

## 📦 NuGet Installation (Quick Start)

If you prefer an even simpler option, you can install the control via NuGet:

```bash
dotnet add package AvaloniaSearchableComboBox
```

Then, configure your App.axaml to include the control's style:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://AvaloniaSearchableComboBox/Themes/SearchableComboBox.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```
Finally, you can use it in your views like this:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:sc="https://searchable-combobox.com">

    <sc:SearchableComboBox Items="{Binding Items}" />

</Window>
```

---

## 🍎 Example with Simple Data (Strings)

### XAML
```xml
<Window
    xmlns:sc="using:AvaloniaSearchableComboBox"
    ...>

<sc:SearchableComboBox
    PlaceholderText="Search Fruit..."
    Width="250"
    ItemsSource="{Binding Fruits}"
    SelectedItem="{Binding SelectedFruit, Mode=TwoWay}" />
````

### ViewModel
```csharp
public class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<string> Fruits { get; set; }

    private string? _selectedFruit;
    public string? SelectedFruit
    {
        get => _selectedFruit;
        set => SetProperty(ref _selectedFruit, value);
    }

    public MainWindowViewModel()
    {
        Fruits = new ObservableCollection<string>
        {
            "Banana",
            "Apple",
            "Peach"
        };
    }
}
```
---

## 🧠 Example with Complex Data

### XAML

```xml
<Window
    xmlns:sc="using:AvaloniaSearchableComboBox"
    ...>

  <StackPanel Spacing="10" Margin="20">
    <sc:SearchableComboBox
        PlaceholderText="Search Country..."
        Width="250"
        ItemsSource="{Binding Countries}"
        SelectedItem="{Binding SelectedCountry, Mode=TwoWay}" />

    <TextBlock Text="{Binding SelectedCountry.Name, StringFormat='Selected Country: {0}'}"/>
    <TextBlock Text="{Binding SelectedCountry.Code, StringFormat='Country Code: {0}'}"/>
    <TextBlock Text="{Binding SelectedCountry.Region, StringFormat='Region: {0}'}"/>
  </StackPanel>
</Window>
```
### ViewModel

```csharp
public class MainWindowViewModel : ViewModelBase
{
    private ObservableCollection<Country> _countries;
    private Country? _selectedCountry;

    public ObservableCollection<Country> Countries
    {
        get => _countries;
        set => SetProperty(ref _countries, value);
    }

    public Country? SelectedCountry
    {
        get => _selectedCountry;
        set
        {
            SetProperty(ref _selectedCountry, value);
            System.Diagnostics.Debug.WriteLine($"SelectedCountry changed to: {value?.Name ?? "null"}");
        }
    }

    public MainWindowViewModel()
    {
        Countries = new ObservableCollection<Country>
        {
            new("Argentina", "AR", "South America"),
            new("Brasil", "BR", "South America"),
            new("Chile", "CL", "South America"),
            new("Colombia", "CO", "South America"),
            new("Costa Rica", "CR", "Central America"),
            new("Ecuador", "EC", "South America"),
            new("México", "MX", "North America"),
            new("Panamá", "PA", "Central America"),
            new("Perú", "PE", "South America"),
            new("Uruguay", "UY", "South America"),
            new("Venezuela", "VE", "South America")
        };
    }
}
```

### Model
```csharp
public class MainWindowViewModel : ViewModelBase
{
    private ObservableCollection<Country> _countries;
    private Country? _selectedCountry;

    public ObservableCollection<Country> Countries
    {
        get => _countries;
        set => SetProperty(ref _countries, value);
    }

    public Country? SelectedCountry
    {
        get => _selectedCountry;
        set
        {
            SetProperty(ref _selectedCountry, value);
            System.Diagnostics.Debug.WriteLine($"SelectedCountry changed to: {value?.Name ?? "null"}");
        }
    }

    public MainWindowViewModel()
    {
        Countries = new ObservableCollection<Country>
        {
            new("Argentina", "AR", "South America"),
            new("Brasil", "BR", "South America"),
            new("Chile", "CL", "South America"),
            new("Colombia", "CO", "South America"),
            new("Costa Rica", "CR", "Central America"),
            new("Ecuador", "EC", "South America"),
            new("México", "MX", "North America"),
            new("Panamá", "PA", "Central America"),
            new("Perú", "PE", "South America"),
            new("Uruguay", "UY", "South America"),
            new("Venezuela", "VE", "South America")
        };
    }
}
```
---

## 🎨 Styling & Customization
Since this is a TemplatedControl, you can modify the visual appearance using the control template defined in SearchableComboBox.axaml. You can also override styles or merge your own theme dictionary for complete visual integration.

---

## 🛠 Requirements
* .NET 8.0 SDK
* Avalonia UI (>=11.3.2)
* Compatible with MVVM Toolkit (e.g., CommunityToolkit.Mvvm)
  

---
## 📄 License

This project is licensed under the MIT License.

---

## 💡 Inspiration
Inspired by the AutoCompleteComboBox behavior from ControlsFX in JavaFX, this control brings similar usability improvements to Avalonia applications.

---

## 🙌 Contributing

Feel free to open issues, fork, and submit pull requests! Contributions and feedback are welcome.
