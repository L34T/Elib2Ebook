using System;
using Core.Configs;

namespace Core.Logic.Getters.Rulate;

public class ErolateGetter(BookGetterConfig config) : RulateGetterBase(config)
{
    protected override Uri SystemUrl => new("https://erolate.com/");
    protected override string Mature => AppSecrets.ErolateMature;
}
