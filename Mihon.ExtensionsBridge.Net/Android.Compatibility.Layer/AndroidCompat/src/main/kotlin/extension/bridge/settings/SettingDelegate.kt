package extension.bridge.settings

import kotlinx.coroutines.flow.MutableStateFlow
import kotlin.reflect.KProperty

open class SettingDelegate<T : Any>(defaultValue: T) {
    private val flow = MutableStateFlow(defaultValue)

    operator fun provideDelegate(thisRef: Any?, property: KProperty<*>): SettingDelegate<T> = this

    operator fun getValue(thisRef: Any?, property: KProperty<*>): MutableStateFlow<T> = flow
}

class BooleanSetting(defaultValue: Boolean) : SettingDelegate<Boolean>(defaultValue)
class IntSetting(defaultValue: Int) : SettingDelegate<Int>(defaultValue)
class StringSetting(defaultValue: String) : SettingDelegate<String>(defaultValue)
