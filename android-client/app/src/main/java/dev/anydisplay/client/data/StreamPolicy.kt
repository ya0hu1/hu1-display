package dev.anydisplay.client.data

enum class HudMode(val key: String) {
    OFF("off"), FPS("fps"), DETAIL("detail");
    companion object { fun from(key: String?) = entries.firstOrNull { it.key == key } ?: OFF }
}

object StreamPolicy {
    // 120 is the controller's highest supported target. This is a request, not a measured rate.
    fun requestHz(manual: Int, panelHz: Int, sustained: Boolean): Int {
        val ceiling = panelHz.coerceIn(1, 120)
        return if (sustained) ceiling else manual.coerceIn(1, ceiling)
    }
}
