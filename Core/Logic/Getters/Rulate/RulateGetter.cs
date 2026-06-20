using System;
using Core.Configs;

namespace Core.Logic.Getters.Rulate;

public class RulateGetter(BookGetterConfig config) : RulateGetterBase(config)
{
    protected override Uri SystemUrl => new("https://tl.rulate.ru");
    protected override string Mature => AppSecrets.RulateMature;
}
