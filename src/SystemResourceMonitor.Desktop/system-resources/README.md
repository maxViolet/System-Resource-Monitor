# System resource package

Place Windows adapters and metric-specific presentation for CPU and RAM here when B05–B06 are implemented. Later accepted GPU and temperature adapters may join this package. Native reads stay off the UI thread according to the implementation plan.

This directory is a source package within the existing Desktop project, not a separate assembly. Shared AppBar, tray, settings, and WPF composition remain outside it.
