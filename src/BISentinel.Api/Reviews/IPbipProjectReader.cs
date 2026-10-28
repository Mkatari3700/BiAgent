namespace BISentinel.Api.Reviews;

public interface IPbipProjectReader
{
    PbipProjectModel Read(string projectPath);
}
