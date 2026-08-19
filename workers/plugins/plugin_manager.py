"""
plugins/plugin_manager.py
Auto-discovers all *_plugin.py files in this folder and registers them.
Adding a new platform = drop a new *_plugin.py file. Zero other changes.
"""
import importlib
import os
import sys
from plugins.base_plugin import BaseDownloaderPlugin


class PluginManager:
    def __init__(self):
        self._plugins: list[BaseDownloaderPlugin] = []

    def discover(self, plugins_dir: str = None):
        """Scan plugins/ folder and register all valid plugins."""
        if plugins_dir is None:
            plugins_dir = os.path.dirname(os.path.abspath(__file__))

        if plugins_dir not in sys.path:
            sys.path.insert(0, os.path.dirname(plugins_dir))

        for fname in sorted(os.listdir(plugins_dir)):
            if not fname.endswith("_plugin.py"):
                continue
            if fname == "base_plugin.py":
                continue
            module_name = f"plugins.{fname[:-3]}"
            try:
                mod = importlib.import_module(module_name)
                for attr_name in dir(mod):
                    cls = getattr(mod, attr_name)
                    if (
                        isinstance(cls, type)
                        and issubclass(cls, BaseDownloaderPlugin)
                        and cls is not BaseDownloaderPlugin
                    ):
                        instance = cls()
                        self._plugins.append(instance)
                        break
            except Exception as e:
                print(f"[PluginManager] Failed to load {fname}: {e}", file=__import__('sys').stderr)

    def find(self, url: str) -> BaseDownloaderPlugin | None:
        """Return the first plugin that can handle the URL."""
        for p in self._plugins:
            if p.can_handle(url):
                return p
        return None

    def list_all(self) -> list[dict]:
        """Return info about all registered plugins."""
        return [{"name": p.name, "icon": p.icon, "domains": p.supported_domains}
                for p in self._plugins]


# Singleton
_manager = PluginManager()


def get_manager() -> PluginManager:
    return _manager
