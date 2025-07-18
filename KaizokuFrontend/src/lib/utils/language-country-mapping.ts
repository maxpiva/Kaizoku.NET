// Language code to country code mapping for flag display
export const languageToCountryMap: Record<string, string> = {
  'af': 'ZA', // Afrikaans -> South Africa
  'ar': 'SA', // Arabic -> Saudi Arabia
  'az': 'AZ', // Azerbaijani -> Azerbaijan
  'be': 'BY', // Belarusian -> Belarus
  'bg': 'BG', // Bulgarian -> Bulgaria
  'bn': 'BD', // Bengali -> Bangladesh
  'ca': 'ES', // Catalan -> Spain (Catalonia)
  'cs': 'CZ', // Czech -> Czech Republic
  'cv': 'RU', // Chuvash -> Russia
  'da': 'DK', // Danish -> Denmark
  'de': 'DE', // German -> Germany
  'el': 'GR', // Greek -> Greece
  'en': 'GB', // English -> United States
  'eo': 'UN', // Esperanto -> Universal/UN flag
  'es': 'ES', // Spanish -> Spain
  'es-419': 'MX', // Latin American Spanish -> Mexico
  'et': 'EE', // Estonian -> Estonia
  'eu': 'ES', // Basque -> Spain
  'fa': 'IR', // Persian -> Iran
  'fi': 'FI', // Finnish -> Finland
  'fil': 'PH', // Filipino -> Philippines
  'fr': 'FR', // French -> France
  'ga': 'IE', // Irish -> Ireland
  'gl': 'ES', // Galician -> Spain
  'he': 'IL', // Hebrew -> Israel
  'hi': 'IN', // Hindi -> India
  'hr': 'HR', // Croatian -> Croatia
  'hu': 'HU', // Hungarian -> Hungary
  'id': 'ID', // Indonesian -> Indonesia
  'it': 'IT', // Italian -> Italy
  'ja': 'JP', // Japanese -> Japan
  'jv': 'ID', // Javanese -> Indonesia
  'ka': 'GE', // Georgian -> Georgia
  'kk': 'KZ', // Kazakh -> Kazakhstan
  'ko': 'KR', // Korean -> Korea
  'la': 'VA', // Latin -> Vatican
  'lt': 'LT', // Lithuanian -> Lithuania
  'lv': 'LV', // Latvian -> Latvia
  'mn': 'MN', // Mongolian -> Mongolia
  'ms': 'MY', // Malay -> Malaysia
  'my': 'MM', // Myanmar -> Myanmar
  'ne': 'NP', // Nepali -> Nepal
  'nl': 'NL', // Dutch -> Netherlands
  'no': 'NO', // Norwegian -> Norway
  'pl': 'PL', // Polish -> Poland
  'pt': 'PT', // Portuguese -> Portugal
  'pt-br': 'BR', // Brazilian Portuguese -> Brazil
  'ro': 'RO', // Romanian -> Romania
  'ru': 'RU', // Russian -> Russia
  'sk': 'SK', // Slovak -> Slovakia
  'sl': 'SI', // Slovenian -> Slovenia
  'sq': 'AL', // Albanian -> Albania
  'sr': 'RS', // Serbian -> Serbia
  'sv': 'SE', // Swedish -> Sweden
  'ta': 'IN', // Tamil -> India
  'te': 'IN', // Telugu -> India
  'th': 'TH', // Thai -> Thailand
  'tl': 'PH', // Filipino -> Philippines
  'tr': 'TR', // Turkish -> Turkey
  'uk': 'UA', // Ukrainian -> Ukraine
  'ur': 'PK', // Urdu -> Pakistan
  'uz': 'UZ', // Uzbek -> Uzbekistan
  'vi': 'VN', // Vietnamese -> Vietnam
  'zh': 'CN', // Chinese -> China
  'zh-hans': 'CN', // Simplified Chinese -> China
  'zh-hant': 'TW', // Traditional Chinese -> Taiwan,
  'all': 'UN'
};

/**
 * Helper function to get country code for a language
 * @param languageCode - The language code to look up
 * @returns The corresponding country code, or 'UN' for unknown languages
 */
export const getCountryCodeForLanguage = (languageCode: string): string => {
  const lowerCode = languageCode.toLowerCase();
  return languageToCountryMap[lowerCode] ?? 'UN'; // Default to UN flag for unknown languages
};
