package extension.bridge.settings

enum class SettingGroup(
    val value: String,
) {
    PROXY("Proxy"),
    CLOUDFLARE("Cloudflare"),
    ;

    override fun toString(): String = value
}
