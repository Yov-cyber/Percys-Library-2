Cómo personalizar el panel "Reading Mode" por tema

Resumen rápido

- Hay un ResourceDictionary central: `Styles/ReadingMode.xaml` que contiene estilos y Storyboards para el panel de lectura.
- Para crear variantes por tema, preferimos crear un ResourceDictionary por tema, por ejemplo:
  - `Themes/CelShade/ReadingMode.xaml`
  - `Themes/Dark/ReadingMode.xaml`

Recomendación de integración

1) Estructura de archivos

- Crea un RD por tema que defina las mismas keys (por ejemplo `ReaderTopBarStyle`, `ReaderOverlayStyle`, `ReaderToggleButtonStyle`) pero con brushes/colores/plantillas distintas.

2) Aplicación dinámica

- El `ThemeManager` ya aplica temas programáticamente; al activar un tema, mezcla los ResourceDictionaries del tema. Asegúrate de que tu RD de tema se cargue después de los RD globales para que sobrescriba claves con la misma key.

3) Estrategia segura

- Evita colisiones de TargetType entre diccionarios: si un `Style` bajo la misma key tiene distintos `TargetType` en distintos RD, WPF puede lanzar excepciones al aplicar estilos. Mantén TargetType consistente.
- Si necesitas variaciones para `Button` vs `ToggleButton`, usa keys distintos como `ReadingMode.ToggleButtonStyle` y `ReadingMode.ButtonStyle`.
- Usa `DynamicResource` desde XAML donde el estilo puede ser resuelto después de aplicar el RD del tema.

4) Animaciones (controladas por ajustes)

- Recomendamos definir las Storyboards en el RD del tema (o en `Styles/ReadingMode.xaml`) pero NO auto-iniciarlas con `BeginStoryboard`/`EventTrigger` cuando la animación debe ser opcional.
- En su lugar, declare las Storyboards como recursos y arránquelas desde código (p. ej. `MainWindow`) sólo si las flags de usuario permiten la animación. Esto evita problemas de orden de carga de ResourceDictionaries y permite deshabilitar animaciones en tiempo de ejecución.
- Flags disponibles en `AppSettings` (persistidas):
  - `EnableAnimations` (master switch)
  - `EnableAnimationsReaderTopBar` (entradas/salidas del top bar)
  - `EnableAnimationsReaderOverlay` (fade in/out del overlay)
  - `EnableAnimationsButtons` (hover/press de botones)
  - `EnableAnimationsPageTurn` (transiciones de cambio de página)
  - `KeepReaderOverlayVisible` (si es true, el overlay/barra del lector no se ocultará automáticamente)

- Patrón recomendado:
  1. Declare la Storyboard en el RD: `<Storyboard x:Key="ReaderTopBarEntrance">...</Storyboard>`
  2. En `MainWindow`, busque el recurso con `TryFindResource("ReaderTopBarEntrance")` y haga `Storyboard.SetTarget(...)` + `Begin()` sólo cuando `SettingsManager.Settings.EnableAnimations && SettingsManager.Settings.EnableAnimationsReaderTopBar`.
  3. Para micro-animaciones de hover (p. ej. escala de botón), prefiera `MultiBinding` + `IMultiValueConverter` que combine `IsMouseOver` con `Application.Current.Resources[AppSettingsProxy].EnableAnimationsButtons` y devuelva el valor visual apropiado (opacidad/scale). Esto evita errores por orden de carga de recursos.

- Ejemplo rápido (pseudo):

  // XAML: declarar Storyboard en `Styles/ReadingMode.xaml`
  `<Storyboard x:Key="ReaderOverlayFadeIn">...</Storyboard>`

  // C#: arrancar condicionalmente
  `var sb = TryFindResource("ReaderOverlayFadeIn") as Storyboard;`
  `if (sb != null && SettingsManager.Settings.EnableAnimations && SettingsManager.Settings.EnableAnimationsReaderOverlay) { Storyboard.SetTarget(sb, overlay); sb.Begin(); } else overlay.Opacity = 0.96;`

Con este enfoque las animaciones son controlables desde la UI de Ajustes y la aplicación se vuelve más robusta ante cambios de tema y orden de carga de recursos.

Ejemplo mínimo (tema):

<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="ReaderBackgroundBrush" Color="#FF0F172A"/>
  <Style x:Key="ReaderTopBarStyle" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource ReaderBackgroundBrush}"/>
    <!-- más setters -->
  </Style>
</ResourceDictionary>

Cómo probar

1. Limpia y build: `dotnet build`.
2. Ejecuta la app y cambia tema desde la UI (Settings) para verificar que el RD del tema impacta el panel de lectura.

Si quieres, creo ahora dos RD de ejemplo (CelShade y Dark) y actualizo `ThemeManager` para que cargue `Themes/<ThemeName>/ReadingMode.xaml` automáticamente cuando se selecciona un tema.
