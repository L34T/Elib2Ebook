using System;
using Core.Configs;

namespace Core.Logic.Getters.Rulate;

public class MlateGetter(BookGetterConfig config) : RulateGetterBase(config)
{
    protected override Uri SystemUrl => new("https://mlate.ru/");
    protected override string Mature => AppSecrets.RulateMature;
}
