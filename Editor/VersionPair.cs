using System;

namespace Hackerzhuli.Code.Editor
{
	internal struct VersionPair
	{
		public Version IdeVersion;
		public Version LanguageVersion;

		public VersionPair(int idemajor, int ideminor, int languageMajor, int languageMinor)
		{
			IdeVersion = new Version(idemajor, ideminor);
			LanguageVersion = new Version(languageMajor, languageMinor);
		}
	}
}
