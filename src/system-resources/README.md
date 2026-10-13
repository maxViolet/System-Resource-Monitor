# System resources package

This package owns the existing system metric Core project and the Windows Desktop application. Core contains platform-independent system metric policy and contracts; Desktop owns WPF, AppBar, native metric readers, settings, tray, and application composition. Keep the two projects and their dependency direction intact.

The Desktop application is the shared process that will compose accepted AI source adapters. AI limit policy and source-specific adapters belong in `../ai-resources/`, not in this package.
