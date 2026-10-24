namespace Tmcs.Icons;

/// <summary>Loads and destroys icon handles. The caller owns every handle <see cref="LoadSmallIcon"/> returns.</summary>
internal interface IIconLoader
{
    /// <returns>An <c>HICON</c> the caller must pass to <see cref="DestroyIcon"/>, or 0 if the path has no loadable icon.</returns>
    nint LoadSmallIcon(string path);

    void DestroyIcon(nint hIcon);
}
