using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using KModkit;

public class MSeqScript : MonoBehaviour {

    public KMBombInfo Bomb;
    public KMAudio Audio;
    public KMBombModule Module;
    public KMSelectable button;
    public GameObject buttonObj;

    public TextMesh number;
    public MeshRenderer color;
    public SpriteRenderer symbol;
    public AudioClip[] clips;
    public AudioClip[] countdownClips;

    public Sprite[] symbols;
    private Color[] colors;

    static int moduleIdCounter = 1;
    int moduleId;

    private Coroutine buttonIn, buttonOut;
    private bool held;
    private const float buttonSpeed = 0.05f;

    private State state;
    private bool[] submission = new bool[10];
    private int pointer = -1;

    private int[][] sequences = new int[3][].Select(x => x = Enumerable.Range(1,9).ToArray()).ToArray();
    private int[] obtainedDigits = new int[3];
    private AudioClip[] selectedClips;
    private int countdownCounter = 0;

    private List<List<int[]>> triangles = new List<List<int[]>>();
    private int finalNumber;
    private bool[] solution;

    private bool tpPressed;

    void Awake ()
    {
        moduleId = moduleIdCounter++;
        colors = new Color[] { "#f14040".Color(), "#ffc500".Color(), "#a53b74".Color(), "#a6be12".Color(), "#33dba0".Color(), "#ff88ff".Color(), "#120074".Color(), "#93c47d".Color(), "#eee5ac".Color() };
        button.OnInteract += delegate () 
        {
            if (held)
                return false;
            if (buttonOut != null)
                StopCoroutine(buttonOut);
            Hold();
            return false;
        };
        
        button.OnInteractEnded += delegate ()
        {
            if (!held)
                return;
            if (buttonIn != null)
                StopCoroutine(buttonIn);
            Release();
            return;
        };
        StartCoroutine(MeasureTimer());

    }

    void Start ()
    {
        GetData();
        finalNumber = CalculateNum(3);
        solution = GenerateSequence(finalNumber);
        DoLogging();
    }

    void GetData()
    {
        for (int i = 0; i < 3; i++)
        {
            sequences[i] = sequences[i].Shuffle();
            obtainedDigits[i] = sequences[i].Last();
        }
        selectedClips = clips.Shuffle().Take(3).ToArray();
    }

    int CalculateNum(int count)
    {
        triangles.Add(Iterate(obtainedDigits));
        for (int i = 0; i < count - 1; i++)
        {
            List<int[]> prev = triangles.Last();
            int[] row = Enumerable.Range(0, prev.Count).Reverse().Select(x => prev[x][0]).ToArray();
            triangles.Add(Iterate(row));
        }
        return triangles.Last().Last().Last();
    }

    List<int[]> Iterate(int[] nums)
    {
        List<int[]> triangle = new List<int[]>() { nums };
        while (triangle.Last().Length != 1)
        {
            int[] row = new int[triangle.Last().Length - 1];
            for (int i = 0; i < row.Length; i++)
                row[i] = triangle.Last()[i] + triangle.Last()[i + 1];
            triangle.Add(row);
        }
        return triangle;
    }

    bool[] GenerateSequence(int input)
    {
        string sequence = string.Empty;
        foreach (int num in input.ToString().Select(x => x - '0'))
            sequence += Convert.ToString(num, 2).PadLeft(2, '0');
        return sequence.PadRight(10, '0').Select(x => x == '1').ToArray();
    }

    void DoLogging()
    {
        Debug.LogFormat("[M-Seq #{0}] The missing entries correspond to digits {1}.", moduleId, obtainedDigits.Join(", "));
        Debug.LogFormat("[M-Seq #{0}] The constructed triangles are as follows:", moduleId);
        foreach (List<int[]> triangle in triangles)
        {
            foreach (int[] row in triangle)
                Debug.LogFormat("[M-Seq #{0}] {1}", moduleId, Enumerable.Repeat(' ', triangle.First().Length - row.Length).Join() + row.Join());
            Debug.LogFormat("[M-Seq #{0}] ", moduleId);
        }
        Debug.LogFormat("[M-Seq #{0}] The generated sequence from {1} is {2}.", moduleId, finalNumber, solution.Select(x => x ? 1 : 0).Join("").TrimEnd('0'));
    }

    void SubmitAnswer()
    {
        Debug.LogFormat("[M-Seq #{0}] You submitted the sequence {1}.", moduleId, submission.Select(x => x ? 1 : 0).Join("").TrimEnd('0'));
        if (submission.SequenceEqual(solution))
        {
            Debug.LogFormat("[M-Seq #{0}] That is correct. Module solved.", moduleId);
            state = State.Solved;
            Module.HandlePass();
            Audio.PlaySoundAtTransform(UnityEngine.Random.Range(0, 30) == 0 ? "easteregg" : "Solve", transform);
            color.gameObject.SetActive(true);
            color.material.color = Color.green;
            number.gameObject.SetActive(true);
            number.gameObject.transform.localPosition = new Vector3(0, 0.57f, 0.01f);
            number.gameObject.transform.localScale = 3 * Vector3.one;
            number.fontStyle = FontStyle.Normal;
            number.text = "!!";
        }
        else
        {
            Debug.LogFormat("[M-Seq #{0}] That is incorrect. Strike incurred and module reverted to initial state.", moduleId);
            Audio.PlaySoundAtTransform("strike", transform);
            Module.HandleStrike();
            Reset();
            state = State.Init;
        }
    }
    void Reset()
    {
        submission = new bool[10];
        countdownCounter = 0;
        pointer = -1;
        selectedClips = clips.Shuffle().Take(3).ToArray();
        for (int i = 0; i < 3; i++)
            sequences[i] = sequences[i].Take(8).ToArray().Shuffle();
    }

    void Hold()
    {
        held = true;  
        button.AddInteractionPunch(0.5f);
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, button.transform);
        buttonIn = StartCoroutine(HoldAnim());
        if (state == State.Submitting && submission.Length != 0 && pointer >= 0)
            submission[pointer] = true;
    }
    void Release()
    {
        held = false;
        button.AddInteractionPunch(0.2f);
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, button.transform);
        buttonIn = StartCoroutine(ReleaseAnim());
        switch (state)
        {
            case State.Init:
                StartCoroutine(PlaySequences());
                state = State.Playing;
                break;
            case State.Idle:
                state = State.Countdown;
                break;
            case State.Submitting:
                if (submission.Length != 0 && pointer >= 0)
                    submission[pointer] = true;
                break;
            case State.Countdown:
                state = State.Init;
                Reset();
                Audio.PlaySoundAtTransform("ResetBeep", transform);
                break;
            default: break;
        }
    }

    IEnumerator PlaySequences()
    {
        yield return new WaitForSecondsRealtime(1);
        Audio.PlaySoundAtTransform(clips[0].name, transform);
        number.gameObject.SetActive(true);
        for (int i = 0; i < 8; i++)
        {
            number.text = sequences[0][i].ToString();
            yield return new WaitForSecondsRealtime(clips[0].length / 8);
        }
        number.gameObject.SetActive(false);

        Audio.PlaySoundAtTransform(clips[1].name, transform);
        color.gameObject.SetActive(true);
        for (int i = 0; i < 8; i++)
        {
            color.material.color = colors[sequences[1][i] - 1];
            yield return new WaitForSecondsRealtime(clips[1].length / 8);
        }
        color.material.color = Color.white;

        Audio.PlaySoundAtTransform(clips[2].name, transform);
        symbol.gameObject.SetActive(true);
        for (int i = 0; i < 8; i++)
        {
            symbol.sprite = symbols[sequences[2][i] - 1];
            yield return new WaitForSecondsRealtime(clips[2].length / 8);
        }
        symbol.gameObject.SetActive(false);
        color.gameObject.SetActive(false);
        state = State.Idle;
    }

    void HandleTimerTick()
    {
        switch (state)
        {
            case State.Countdown:
                Audio.PlaySoundAtTransform(countdownClips[countdownCounter].name, transform);
                countdownCounter++;
                if (countdownCounter == 4)
                {
                    state = State.Submitting;
                    countdownCounter = 0;
                }
                break;
            case State.Submitting:
                pointer++;
                if (pointer > 10)
                    SubmitAnswer();
                tpPressed = false;
                break;
            default: break;
        }
    }
    
    IEnumerator MeasureTimer()
    {
        int prevTime;
        do
        {
            prevTime = (int)Bomb.GetTime();
            yield return null;
            if ((int)Bomb.GetTime() != prevTime)
                HandleTimerTick();
        } while (state != State.Solved);
    }

    IEnumerator HoldAnim()
    {
        while (buttonObj.transform.localPosition.y - buttonSpeed * Time.deltaTime > -0.005f)
        {
            buttonObj.transform.localPosition += buttonSpeed * Time.deltaTime * Vector3.down;
            yield return null;
        }
        buttonObj.transform.localPosition = new Vector3(0, -0.005f, 0);
    }
    IEnumerator ReleaseAnim()
    {
        while (buttonObj.transform.localPosition.y + buttonSpeed * Time.deltaTime < 0)
        {
            buttonObj.transform.localPosition += buttonSpeed * Time.deltaTime * Vector3.up;
            yield return null;
        }
        buttonObj.transform.localPosition = Vector3.zero;
    }

    #pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Use [!{0} play] to play the sequence. Use [!{0} submit 01101101] to submit that sequence into the module. Use [!{0} reset] to reset to the initial";
#pragma warning restore 414

    IEnumerator ToggleButton()
    {
        yield return new WaitForSeconds(0.1f);
        if (!held)
            button.OnInteract();
        else button.OnInteractEnded();
        yield return new WaitForSeconds(0.1f);
    }
    IEnumerator TapButton()
    {
        yield return ToggleButton();
        yield return ToggleButton();
    }
    IEnumerator ProcessTwitchCommand(string command)
    {
        command = command.Trim().ToUpperInvariant();
        string[] parameters = command.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (command == "PLAY")
        {
            if (state != State.Init)
                yield return "sendtochaterror You cannot play the sequence at this time.";
            else
            {
                yield return null;
                yield return TapButton();
            }
        }
        else if (command == "RESET")
        {
            if (state != State.Idle)
                yield return "sendtochaterror You cannot reset the module at this time.";
            else
            {
                yield return null;
                yield return TapButton();
                yield return new WaitUntil(() => state == State.Countdown);
                yield return new WaitForSeconds(0.1f);
                yield return TapButton();
            }
        }
        else if (parameters.Length == 2 && parameters.First() == "SUBMIT" && parameters.Last().All(x => "01".Contains(x)))
        {
            if (state != State.Idle)
                yield return "sendtochaterror You cannot submit an answer at this time.";
            else
            {
                yield return null;
                bool[] submitting = parameters.Last().TrimEnd('0').Select(x => x == '1').ToArray();
                yield return TapButton();
                while (state != State.Submitting)
                    yield return null;
                while (pointer < submitting.Length)
                {
                    if (pointer != -1 && submitting[pointer] && !tpPressed)
                    {
                        tpPressed = true;
                        yield return ToggleButton();
                    }
                    yield return null;
                }
                yield return submission.SequenceEqual(solution) ? "solve" : "strike";
            }
        }
    }

    IEnumerator TwitchHandleForcedSolve ()
    {
        bool[] submitting = solution.Select(x => x ? "1" : "0").Join("").TrimEnd('0').Select(x => x == '1').ToArray();
        if (state == State.Init)
            yield return TapButton();
        while (state == State.Playing)
            yield return true;
        yield return TapButton();

        while (pointer < submitting.Length)
        {
            if (pointer != -1 && submitting[pointer] && !tpPressed)
            {
                tpPressed = true;
                yield return ToggleButton();
            }
            yield return null;
        }
        while (state != State.Solved)
            yield return true;
    }
}
