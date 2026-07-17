using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Skua.WPF;

[ContentProperty("DataTemplates")]
public class PropertyGridDataTemplateSelector : DataTemplateSelector
{
    public PropertyGridDataTemplateSelector()
    {
        DataTemplates = new ObservableCollection<PropertyGridDataTemplate>();
    }

    public PropertyGrid PropertyGrid { get; private set; }

    protected virtual bool Filter(PropertyGridDataTemplate template, PropertyGridProperty property)
    {
        if (template == null)
            throw new ArgumentNullException("template");

        if (property == null)
            throw new ArgumentNullException("property");

        // check various filters
        if (template.IsCollection.HasValue && template.IsCollection.Value != property.IsCollection)
        {
            return true;
        }

        if (template.IsCollectionItemValueType.HasValue && template.IsCollectionItemValueType.Value != property.IsCollectionItemValueType)
        {
            return true;
        }

        if (template.IsValueType.HasValue && template.IsValueType.Value != property.IsValueType)
        {
            return true;
        }

        if (template.IsReadOnly.HasValue && template.IsReadOnly.Value != property.IsReadOnly)
        {
            return true;
        }

        if (template.IsError.HasValue && template.IsError.Value != property.IsError)
        {
            return true;
        }

        return template.IsValid.HasValue && template.IsValid.Value != property.IsValid || (template.IsFlagsEnum.HasValue && template.IsFlagsEnum.Value != property.IsFlagsEnum) || (template.Category != null && !property.Category.EqualsIgnoreCase(template.Category)) || (template.Name != null && !property.Name.EqualsIgnoreCase(template.Name));
    }

    public virtual bool IsAssignableFrom(Type type, Type propertyType, PropertyGridDataTemplate template, PropertyGridProperty property)
    {
        if (type == null)
            throw new ArgumentNullException("type");

        if (propertyType == null)
            throw new ArgumentNullException("propertyType");

        if (template == null)
            throw new ArgumentNullException("template");

        if (property == null)
            throw new ArgumentNullException("property");

        if (type.IsAssignableFrom(propertyType))
        {
            // bool? is assignable from bool, but we don't want that match
            if (!type.IsNullable() || propertyType.IsNullable())
                return true;
        }

        if (type == PropertyGridDataTemplate.NullableEnumType)
        {
            PropertyGridProperty.IsEnumOrNullableEnum(propertyType, out Type enumType, out bool nullable);
            if (nullable)
                return true;
        }

        PropertyGridOptionsAttribute options = PropertyGridOptionsAttribute.FromProperty(property);
        if (options != null)
        {
            if ((type.IsEnum || type == typeof(Enum)) && options.IsEnum)
            {
                if (!options.IsFlagsEnum)
                    return true;

                if (Extensions.IsFlagsEnum(type))
                    return true;

                if (template.IsFlagsEnum.HasValue && template.IsFlagsEnum.Value)
                    return true;
            }
        }

        return false;
    }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (container == null)
            throw new ArgumentNullException("container");

        if (item is not PropertyGridProperty property)
            return base.SelectTemplate(item, container);

        DataTemplate propTemplate = PropertyGridOptionsAttribute.SelectTemplate(property, item, container);
        if (propTemplate != null)
            return propTemplate;

        PropertyGrid ??= container.GetVisualSelfOrParent<PropertyGrid>();

        if (PropertyGrid.ValueEditorTemplateSelector != null && PropertyGrid.ValueEditorTemplateSelector != this)
        {
            DataTemplate template = PropertyGrid.ValueEditorTemplateSelector.SelectTemplate(item, container);
            if (template != null)
                return template;
        }

        foreach (PropertyGridDataTemplate template in DataTemplates)
        {
            if (Filter(template, property))
                continue;

            if (template.IsCollection.HasValue && template.IsCollection.Value)
            {
                if (string.IsNullOrWhiteSpace(template.CollectionItemPropertyType) && template.DataTemplate != null)
                    return template.DataTemplate;

                if (property.CollectionItemPropertyType != null)
                {
                    foreach (Type type in template.ResolvedCollectionItemPropertyTypes)
                    {
                        if (IsAssignableFrom(type, property.CollectionItemPropertyType, template, property))
                            return template.DataTemplate;
                    }
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(template.PropertyType) && template.DataTemplate != null)
                    return template.DataTemplate;

                foreach (Type type in template.ResolvedPropertyTypes)
                {
                    if (IsAssignableFrom(type, property.PropertyType, template, property))
                        return template.DataTemplate;
                }
            }
        }
        return base.SelectTemplate(item, container);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollection<PropertyGridDataTemplate> DataTemplates { get; private set; }
}