using WindowsAxon.Eval;

namespace WindowsAxon.Agent.Tests.Eval;

public class ReportFileNameTests
{
    private static readonly DateTime Time = new(2026, 10, 10, 13, 45, 7);

    [Theory]
    [InlineData("qwen3:8b", "20261010-134507-qwen3_8b.json")]
    [InlineData("meta-llama/llama-3.3-70b-instruct", "20261010-134507-meta-llama_llama-3.3-70b-instruct.json")]
    [InlineData("hf.co/user/model:tag", "20261010-134507-hf.co_user_model_tag.json")]
    [InlineData(@"a\b*c?d""e<f>g|h", "20261010-134507-a_b_c_d_e_f_g_h.json")]
    public void Model_name_becomes_a_single_safe_file_name(string model, string expected)
    {
        Assert.Equal(expected, ReportFileName.For(model, Time));
    }

    [Fact]
    public void File_name_has_no_directory_part_or_invalid_chars()
    {
        var name = ReportFileName.For(@"org/sub\model:tag", Time);

        Assert.Equal(name, Path.GetFileName(name));
        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
    }
}
