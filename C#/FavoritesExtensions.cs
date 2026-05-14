/*
 * FavoritesExtensions.cs
 * Smart Grand Egyptian Museum — HCI Interactive Table
 * 
 * Extension methods for easy integration of favorites functionality
 * with existing VisitorProfile, FigureDef, RelationshipDef, and SceneObjectDef classes.
 * 
 * Usage Examples:
 * 
 *   // Check if a figure is favorited
 *   bool isFav = figure.IsFavorited(profile, favoritesManager);
 * 
 *   // Toggle favorite status
 *   figure.ToggleFavorite(profile, favoritesManager);
 * 
 *   // Add a relationship to favorites
 *   relationship.AddToFavorites(profile, favoritesManager);
 * 
 *   // Get all favorited figures for a user
 *   var favFigures = profile.GetFavoriteFigures(favoritesManager);
 */

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Extension methods for VisitorProfile to work with favorites.
/// </summary>
public static class VisitorProfileFavoritesExtensions
{
    /// <summary>
    /// Gets all favorite figures for this visitor.
    /// </summary>
    public static List<FigureDef> GetFavoriteFigures(this VisitorProfile profile, FavoritesManager manager)
    {
        if (profile == null || manager == null)
            return new List<FigureDef>();

        var favItems = manager.GetFavoritesByType(profile, FavoriteType.Figure);
        var figures = new List<FigureDef>();

        foreach (var item in favItems)
        {
            // Identifier format: "Figure: Name" or just "Name"
            string figureName = item.Identifier.Replace("Figure: ", "").Trim();
            
            var figure = MuseumData.Figures.Values
                .FirstOrDefault(f => string.Equals(f.Name, figureName, StringComparison.OrdinalIgnoreCase));
            
            if (figure != null)
                figures.Add(figure);
        }

        return figures;
    }

    /// <summary>
    /// Gets all favorite relationships for this visitor.
    /// </summary>
    public static List<RelationshipDef> GetFavoriteRelationships(this VisitorProfile profile, FavoritesManager manager)
    {
        if (profile == null || manager == null)
            return new List<RelationshipDef>();

        var favItems = manager.GetFavoritesByType(profile, FavoriteType.Relationship);
        var relationships = new List<RelationshipDef>();

        foreach (var item in favItems)
        {
            // Identifier format: "Connection: Title" or just "Title"
            string title = item.Identifier.Replace("Connection: ", "").Trim();
            
            var relationship = MuseumData.Relationships
                .FirstOrDefault(r => string.Equals(r.ConnectionTitle, title, StringComparison.OrdinalIgnoreCase));
            
            if (relationship != null)
                relationships.Add(relationship);
        }

        return relationships;
    }

    /// <summary>
    /// Gets all favorite objects for this visitor.
    /// Returns a list of tuples containing (FigureDef, SceneObjectDef).
    /// </summary>
    public static List<Tuple<FigureDef, SceneObjectDef>> GetFavoriteObjects(this VisitorProfile profile, FavoritesManager manager)
    {
        if (profile == null || manager == null)
            return new List<Tuple<FigureDef, SceneObjectDef>>();

        var favItems = manager.GetFavoritesByType(profile, FavoriteType.Object);
        var objects = new List<Tuple<FigureDef, SceneObjectDef>>();

        foreach (var item in favItems)
        {
            // Identifier format: "FigureName - ObjectName"
            var parts = item.Identifier.Split(new[] { " - " }, StringSplitOptions.None);
            if (parts.Length != 2)
                continue;

            string figureName = parts[0].Trim();
            string objectName = parts[1].Trim();

            var figure = MuseumData.Figures.Values
                .FirstOrDefault(f => string.Equals(f.Name, figureName, StringComparison.OrdinalIgnoreCase));

            if (figure != null && figure.SceneObjects != null)
            {
                var obj = figure.SceneObjects
                    .FirstOrDefault(o => string.Equals(o.Name, objectName, StringComparison.OrdinalIgnoreCase));

                if (obj != null)
                    objects.Add(Tuple.Create(figure, obj));
            }
        }

        return objects;
    }

    /// <summary>
    /// Gets the total count of favorites for this visitor.
    /// </summary>
    public static int GetFavoritesCount(this VisitorProfile profile, FavoritesManager manager)
    {
        if (profile == null || manager == null)
            return 0;

        return manager.GetFavoritesCount(profile);
    }

    /// <summary>
    /// Checks if this visitor has any favorites.
    /// </summary>
    public static bool HasFavorites(this VisitorProfile profile, FavoritesManager manager)
    {
        return profile.GetFavoritesCount(manager) > 0;
    }
}

/// <summary>
/// Extension methods for FigureDef to work with favorites.
/// </summary>
public static class FigureDefFavoritesExtensions
{
    /// <summary>
    /// Gets the favorite identifier for this figure.
    /// Format: "Figure: Name"
    /// </summary>
    public static string GetFavoriteIdentifier(this FigureDef figure)
    {
        if (figure == null)
            return string.Empty;
        return $"Figure: {figure.Name}";
    }

    /// <summary>
    /// Checks if this figure is favorited by the visitor.
    /// </summary>
    public static bool IsFavorited(this FigureDef figure, VisitorProfile profile, FavoritesManager manager)
    {
        if (figure == null || profile == null || manager == null)
            return false;

        return manager.IsFavorite(profile, FavoriteType.Figure, figure.GetFavoriteIdentifier());
    }

    /// <summary>
    /// Adds this figure to the visitor's favorites.
    /// </summary>
    public static bool AddToFavorites(this FigureDef figure, VisitorProfile profile, FavoritesManager manager)
    {
        if (figure == null || profile == null || manager == null)
            return false;

        return manager.AddFavorite(profile, FavoriteType.Figure, figure.GetFavoriteIdentifier());
    }

    /// <summary>
    /// Removes this figure from the visitor's favorites.
    /// </summary>
    public static bool RemoveFromFavorites(this FigureDef figure, VisitorProfile profile, FavoritesManager manager)
    {
        if (figure == null || profile == null || manager == null)
            return false;

        return manager.RemoveFavorite(profile, FavoriteType.Figure, figure.GetFavoriteIdentifier());
    }

    /// <summary>
    /// Toggles the favorite status of this figure.
    /// Returns true if now favorited, false if unfavorited.
    /// </summary>
    public static bool ToggleFavorite(this FigureDef figure, VisitorProfile profile, FavoritesManager manager)
    {
        if (figure == null || profile == null || manager == null)
            return false;

        return manager.ToggleFavorite(profile, FavoriteType.Figure, figure.GetFavoriteIdentifier());
    }
}

/// <summary>
/// Extension methods for RelationshipDef to work with favorites.
/// </summary>
public static class RelationshipDefFavoritesExtensions
{
    /// <summary>
    /// Gets the favorite identifier for this relationship.
    /// Format: "Connection: Title"
    /// </summary>
    public static string GetFavoriteIdentifier(this RelationshipDef relationship)
    {
        if (relationship == null)
            return string.Empty;
        return $"Connection: {relationship.ConnectionTitle}";
    }

    /// <summary>
    /// Checks if this relationship is favorited by the visitor.
    /// </summary>
    public static bool IsFavorited(this RelationshipDef relationship, VisitorProfile profile, FavoritesManager manager)
    {
        if (relationship == null || profile == null || manager == null)
            return false;

        return manager.IsFavorite(profile, FavoriteType.Relationship, relationship.GetFavoriteIdentifier());
    }

    /// <summary>
    /// Adds this relationship to the visitor's favorites.
    /// </summary>
    public static bool AddToFavorites(this RelationshipDef relationship, VisitorProfile profile, FavoritesManager manager)
    {
        if (relationship == null || profile == null || manager == null)
            return false;

        return manager.AddFavorite(profile, FavoriteType.Relationship, relationship.GetFavoriteIdentifier());
    }

    /// <summary>
    /// Removes this relationship from the visitor's favorites.
    /// </summary>
    public static bool RemoveFromFavorites(this RelationshipDef relationship, VisitorProfile profile, FavoritesManager manager)
    {
        if (relationship == null || profile == null || manager == null)
            return false;

        return manager.RemoveFavorite(profile, FavoriteType.Relationship, relationship.GetFavoriteIdentifier());
    }

    /// <summary>
    /// Toggles the favorite status of this relationship.
    /// Returns true if now favorited, false if unfavorited.
    /// </summary>
    public static bool ToggleFavorite(this RelationshipDef relationship, VisitorProfile profile, FavoritesManager manager)
    {
        if (relationship == null || profile == null || manager == null)
            return false;

        return manager.ToggleFavorite(profile, FavoriteType.Relationship, relationship.GetFavoriteIdentifier());
    }
}

/// <summary>
/// Extension methods for SceneObjectDef to work with favorites.
/// </summary>
public static class SceneObjectDefFavoritesExtensions
{
    /// <summary>
    /// Gets the favorite identifier for this scene object.
    /// Format: "FigureName - ObjectName"
    /// </summary>
    public static string GetFavoriteIdentifier(this SceneObjectDef obj, FigureDef parentFigure)
    {
        if (obj == null || parentFigure == null)
            return string.Empty;
        return $"{parentFigure.Name} - {obj.Name}";
    }

    /// <summary>
    /// Checks if this scene object is favorited by the visitor.
    /// </summary>
    public static bool IsFavorited(this SceneObjectDef obj, FigureDef parentFigure, VisitorProfile profile, FavoritesManager manager)
    {
        if (obj == null || parentFigure == null || profile == null || manager == null)
            return false;

        return manager.IsFavorite(profile, FavoriteType.Object, obj.GetFavoriteIdentifier(parentFigure));
    }

    /// <summary>
    /// Adds this scene object to the visitor's favorites.
    /// </summary>
    public static bool AddToFavorites(this SceneObjectDef obj, FigureDef parentFigure, VisitorProfile profile, FavoritesManager manager)
    {
        if (obj == null || parentFigure == null || profile == null || manager == null)
            return false;

        return manager.AddFavorite(profile, FavoriteType.Object, obj.GetFavoriteIdentifier(parentFigure));
    }

    /// <summary>
    /// Removes this scene object from the visitor's favorites.
    /// </summary>
    public static bool RemoveFromFavorites(this SceneObjectDef obj, FigureDef parentFigure, VisitorProfile profile, FavoritesManager manager)
    {
        if (obj == null || parentFigure == null || profile == null || manager == null)
            return false;

        return manager.RemoveFavorite(profile, FavoriteType.Object, obj.GetFavoriteIdentifier(parentFigure));
    }

    /// <summary>
    /// Toggles the favorite status of this scene object.
    /// Returns true if now favorited, false if unfavorited.
    /// </summary>
    public static bool ToggleFavorite(this SceneObjectDef obj, FigureDef parentFigure, VisitorProfile profile, FavoritesManager manager)
    {
        if (obj == null || parentFigure == null || profile == null || manager == null)
            return false;

        return manager.ToggleFavorite(profile, FavoriteType.Object, obj.GetFavoriteIdentifier(parentFigure));
    }
}
