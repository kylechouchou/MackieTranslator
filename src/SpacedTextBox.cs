using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MackieTranslator {
    // Plain-text editing with real paragraph line-height support; pasted formatting is discarded.
    public sealed class SpacedTextBox:RichTextBox {
        string text="";
        public string Text {
            get{return text;}
            set{string normalized=Normalize(value??"");if(normalized==text)return;
                BeginChange();try{if(text.Length>0&&normalized.StartsWith(text,StringComparison.Ordinal)){var end=Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);new TextRange(end,end).Text=normalized.Substring(text.Length);}else{Document.Blocks.Clear();Document.Blocks.Add(new Paragraph(new Run(normalized)){Margin=new Thickness(0)});}}finally{EndChange();}
            }
        }
        public int CaretIndex {get{return Normalize(new TextRange(Document.ContentStart,CaretPosition).Text).Length;}set{CaretPosition=value<=0?Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward):Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);}}
        public SpacedTextBox(){
            AllowDrop=false;IsDocumentEnabled=false;IsUndoEnabled=false;
            Document=new FlowDocument {PagePadding=new Thickness(0),IsHyphenationEnabled=false};
            var paragraphs=new Style(typeof(Paragraph));paragraphs.Setters.Add(new Setter(Block.MarginProperty,new Thickness(0)));Document.Resources.Add(typeof(Paragraph),paragraphs);Document.Blocks.Add(new Paragraph(new Run("")){Margin=new Thickness(0)});
            DataObject.AddPastingHandler(this,(sender,e)=>{if(!e.DataObject.GetDataPresent(DataFormats.UnicodeText)){e.CancelCommand();return;}e.DataObject=new DataObject(DataFormats.UnicodeText,Convert.ToString(e.DataObject.GetData(DataFormats.UnicodeText)));});
            CommandBindings.Add(new System.Windows.Input.CommandBinding(EditingCommands.ToggleBold,(s,e)=>e.Handled=true));
            CommandBindings.Add(new System.Windows.Input.CommandBinding(EditingCommands.ToggleItalic,(s,e)=>e.Handled=true));
            CommandBindings.Add(new System.Windows.Input.CommandBinding(EditingCommands.ToggleUnderline,(s,e)=>e.Handled=true));
        }
        static string Normalize(string value){return value.Replace("\r\n","\n").Replace('\r','\n');}
        protected override void OnTextChanged(TextChangedEventArgs e){string next=Normalize(new TextRange(Document.ContentStart,Document.ContentEnd).Text);if(next.EndsWith("\n"))next=next.Substring(0,next.Length-1);if(next==text)return;text=next;base.OnTextChanged(e);}
        public void Clear(){Text="";}
        public void SetReadingFormat(FontFamily family,double size,FontWeight weight,FontStyle style,string mode,double points,double multiple){
            FontFamily=Document.FontFamily=family;FontSize=Document.FontSize=size;FontWeight=Document.FontWeight=weight;FontStyle=Document.FontStyle=style;
            double natural=size*family.LineSpacing;
            Document.LineStackingStrategy=mode=="固定值"?LineStackingStrategy.BlockLineHeight:LineStackingStrategy.MaxHeight;
            Document.LineHeight=mode=="单倍行距"?double.NaN:mode=="1.5倍行距"?natural*1.5:mode=="双倍行距"?natural*2:mode=="多倍行距"?natural*multiple:points*96.0/72.0;
        }
    }
}

